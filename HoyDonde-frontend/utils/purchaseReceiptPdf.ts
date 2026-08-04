import * as Print from 'expo-print';
import * as Sharing from 'expo-sharing';
import { File, Paths } from 'expo-file-system';

import { escapeHtml } from './reportPdf';
import { formatFechaHora, formatPrecio } from './format';
import { buildTicketQrPayload } from './ticketQr';
import { buildQrSvgMarkup } from './qrSvg';
import type { CompraResponse, TicketResponse } from '@/services/APIService';

/**
 * Comprobante PDF de una compra simulada recién completada (docs/api-mvp-plan.md §14): se arma
 * enteramente en el frontend, a partir de la CompraResponse que ya devolvió POST /api/tickets/buy
 * — nunca una nueva lectura de precio/evento/ubicación. `Compra` (evento, ubicación, fechas,
 * fecha de compra, cantidad e importe total) es la única fuente del encabezado; sus `Ticket` son
 * el detalle, agrupados por tipo. Nunca se recalcula el total para reemplazar al backend: si la
 * suma de los tickets no coincide con `Compra.ImporteTotal`, no se genera un comprobante
 * engañoso (ver `assertCompraConsistente`).
 */

const DISCLAIMER_PAGO_SIMULADO =
  'La operación utiliza un pago simulado. El importe emitido no acredita dinero cobrado ni reemplaza una factura o recibo fiscal.';

/** Nunca se genera un comprobante con datos que no cuadran entre sí — la compra ya realizada sigue siendo válida. */
export class PurchaseReceiptInconsistencyError extends Error {
  constructor() {
    super('Los datos de la compra no son consistentes.');
    this.name = 'PurchaseReceiptInconsistencyError';
  }
}

/**
 * La cantidad/importe mostrados en el comprobante siempre salen de `Compra` (calculados por el
 * backend dentro de la transacción de compra), nunca de recalcular sobre los `Ticket` — esta
 * verificación solo confirma que ambas fuentes coinciden, nunca reemplaza a `Compra` como fuente.
 */
export function assertCompraConsistente(compra: CompraResponse): void {
  const totalesTickets = computePurchaseTotals(compra.tickets);
  if (
    compra.tickets.length !== compra.cantidadEntradas ||
    totalesTickets.cantidadTotal !== compra.cantidadEntradas ||
    totalesTickets.importeTotal !== compra.importeTotal
  ) {
    throw new PurchaseReceiptInconsistencyError();
  }
}

export interface PurchaseReceiptTicketGroup {
  ticketTypeId: string;
  ticketTypeNombre: string;
  cantidad: number;
  precioUnitario: number;
  subtotal: number;
}

/**
 * Agrupa por TicketTypeId. Cantidad/precio unitario/subtotal salen exclusivamente de
 * `PrecioPagado` de cada Ticket (fotografía inmutable tomada en la compra) — nunca se vuelve a
 * consultar el precio actual del Event ni se acepta un total calculado antes de comprar.
 */
export function groupPurchaseTickets(tickets: TicketResponse[]): PurchaseReceiptTicketGroup[] {
  const groups: PurchaseReceiptTicketGroup[] = [];
  const indexByTipo = new Map<string, number>();

  for (const ticket of tickets) {
    const idx = indexByTipo.get(ticket.ticketTypeId);
    if (idx === undefined) {
      indexByTipo.set(ticket.ticketTypeId, groups.length);
      groups.push({
        ticketTypeId: ticket.ticketTypeId,
        ticketTypeNombre: ticket.ticketTypeNombre,
        cantidad: 1,
        precioUnitario: ticket.precioPagado,
        subtotal: ticket.precioPagado,
      });
    } else {
      groups[idx].cantidad += 1;
      groups[idx].subtotal += ticket.precioPagado;
    }
  }

  return groups;
}

export interface PurchaseReceiptTotals {
  cantidadTotal: number;
  importeTotal: number;
}

export function computePurchaseTotals(tickets: TicketResponse[]): PurchaseReceiptTotals {
  return {
    cantidadTotal: tickets.length,
    importeTotal: tickets.reduce((sum, t) => sum + t.precioPagado, 0),
  };
}

export function formatPrecioOGratis(value: number): string {
  return value === 0 ? 'GRATIS' : formatPrecio(value);
}

/** Nombre de archivo a partir de la fecha de compra (fotografía del Ticket, nunca la fecha de generación del PDF). */
export function buildReceiptFileName(fechaCompraIso: string): string {
  const date = new Date(fechaCompraIso);
  const iso = Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString();
  return `HoyDonde-Comprobante-${iso.slice(0, 10)}.pdf`;
}

function buildTicketSectionHtml(ticket: TicketResponse, index: number, total: number): string {
  const payload = buildTicketQrPayload(ticket.id, ticket.eventoId);
  const qrSvg = buildQrSvgMarkup(payload, { size: 190 });

  return `
  <div class="ticket">
    <div class="ticketHeader">
      <span class="ticketIndex">ENTRADA ${index + 1} DE ${total}</span>
      <span class="ticketTipo">${escapeHtml(ticket.ticketTypeNombre)}</span>
    </div>
    <div class="ticketId">ID de la entrada: <span class="mono">${escapeHtml(ticket.id)}</span></div>
    <div class="qrWrap">${qrSvg}</div>
    <p class="ticketHint">Presentá este código QR al Control del evento para validar esta entrada. Es de un solo uso: no lo compartas.</p>
  </div>`;
}

export function buildPurchaseReceiptHtml(compra: CompraResponse): string {
  if (compra.tickets.length === 0) {
    throw new Error('No hay entradas para armar el comprobante.');
  }
  // Nunca genera un comprobante engañoso: si la suma de los Ticket no coincide con Compra, se
  // detiene acá — la compra ya realizada no se toca, el llamador decide cómo informar el error.
  assertCompraConsistente(compra);

  const { tickets } = compra;
  const grupos = groupPurchaseTickets(tickets);
  const generadoEl = new Date().toLocaleString('es-AR');

  const detalleFilas = grupos
    .map(
      (g) => `<tr>
        <td>${escapeHtml(g.ticketTypeNombre)}</td>
        <td>${g.cantidad}</td>
        <td>${escapeHtml(formatPrecioOGratis(g.precioUnitario))}</td>
        <td>${escapeHtml(formatPrecioOGratis(g.subtotal))}</td>
      </tr>`
    )
    .join('');

  const ticketsHtml = tickets.map((t, i) => buildTicketSectionHtml(t, i, tickets.length)).join('');

  return `<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8" />
<style>
  * { box-sizing: border-box; }
  body {
    font-family: Georgia, 'Times New Roman', serif;
    color: #171512;
    background: #F3EBDD;
    margin: 0;
    padding: 32px;
  }
  .stamp {
    display: inline-block;
    border: 2px solid #F04E3E;
    color: #F04E3E;
    font-weight: bold;
    font-size: 12px;
    letter-spacing: 1.5px;
    padding: 4px 10px;
    margin-bottom: 8px;
  }
  .eyebrow {
    font-size: 12px;
    letter-spacing: 2px;
    text-transform: uppercase;
    color: #6B6357;
    font-weight: bold;
  }
  h1 {
    font-size: 24px;
    margin: 4px 0 16px;
    border-bottom: 3px solid #171512;
    padding-bottom: 12px;
  }
  .metaRow {
    font-size: 12px;
    color: #6B6357;
    margin-bottom: 2px;
  }
  .section-title {
    font-size: 15px;
    font-weight: bold;
    margin: 22px 0 8px;
    border-bottom: 1px solid #171512;
    padding-bottom: 4px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 12px;
  }
  th, td {
    border: 1px solid #D8CDBB;
    padding: 6px 8px;
    text-align: left;
  }
  table thead th {
    background: #D8CDBB;
    text-transform: uppercase;
    font-size: 10px;
    letter-spacing: 0.5px;
  }
  .infoTable th, .totalsTable th {
    background: #D8CDBB;
    width: 40%;
  }
  .totalsTable {
    margin-top: 10px;
  }
  .totalsTable tr:last-child th, .totalsTable tr:last-child td {
    font-weight: bold;
    color: #F04E3E;
  }
  .disclaimer {
    margin-top: 20px;
    padding: 12px;
    border: 1px dashed #6B6357;
    font-size: 11px;
    color: #6B6357;
    font-style: italic;
  }
  .ticket {
    break-inside: avoid;
    page-break-inside: avoid;
    border: 1px solid #171512;
    border-radius: 4px;
    padding: 16px;
    margin-top: 14px;
  }
  .ticketHeader {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    border-bottom: 1px solid #D8CDBB;
    padding-bottom: 8px;
    margin-bottom: 8px;
  }
  .ticketIndex {
    font-size: 11px;
    letter-spacing: 1px;
    color: #6B6357;
    font-weight: bold;
  }
  .ticketTipo {
    font-size: 15px;
    font-weight: bold;
    color: #F04E3E;
    text-transform: uppercase;
  }
  .ticketId {
    font-size: 11px;
    color: #6B6357;
    margin-bottom: 10px;
  }
  .mono {
    font-family: 'Courier New', monospace;
    color: #171512;
  }
  .qrWrap {
    width: fit-content;
    margin: 0 auto;
    padding: 12px;
    background: #FFFFFF;
    border: 1px solid #171512;
  }
  .ticketHint {
    font-size: 11px;
    color: #6B6357;
    text-align: center;
    margin: 10px 0 0;
  }
</style>
</head>
<body>
  <div class="stamp">COMPROBANTE NO FISCAL</div>
  <div class="eyebrow">HOYDONDE?</div>
  <h1>COMPROBANTE DE COMPRA SIMULADA</h1>

  <div class="metaRow">N.º DE OPERACIÓN: ${escapeHtml(compra.id)}</div>
  <div class="metaRow">Generado el ${escapeHtml(generadoEl)}</div>
  <div class="metaRow">Fecha de compra: ${escapeHtml(formatFechaHora(compra.fechaCompra))}</div>

  <div class="section-title">Evento</div>
  <table class="infoTable">
    <tbody>
      <tr><th>Evento</th><td>${escapeHtml(compra.eventoNombre)}</td></tr>
      <tr><th>Ubicación</th><td>${escapeHtml(compra.ubicacion)}</td></tr>
      <tr><th>Inicio</th><td>${escapeHtml(formatFechaHora(compra.fechaInicio))}</td></tr>
      <tr><th>Fin</th><td>${escapeHtml(formatFechaHora(compra.fechaFin))}</td></tr>
    </tbody>
  </table>

  <div class="section-title">Detalle de la compra</div>
  <table>
    <thead>
      <tr><th>Tipo de entrada</th><th>Cantidad</th><th>Precio unitario</th><th>Subtotal</th></tr>
    </thead>
    <tbody>${detalleFilas}</tbody>
  </table>
  <table class="totalsTable">
    <tbody>
      <tr><th>Cantidad total de entradas</th><td>${compra.cantidadEntradas}</td></tr>
      <tr><th>Importe total emitido</th><td>${escapeHtml(formatPrecioOGratis(compra.importeTotal))}</td></tr>
      <tr><th>Pago</th><td>${compra.pagoSimulado ? 'Simulado' : 'No simulado'}</td></tr>
    </tbody>
  </table>

  <div class="disclaimer">
    <strong>COMPROBANTE NO FISCAL.</strong> ${escapeHtml(DISCLAIMER_PAGO_SIMULADO)}
  </div>

  <div class="section-title">Entradas (${tickets.length})</div>
  ${ticketsHtml}
</body>
</html>`;
}

export interface GeneratePurchaseReceiptPdfResult {
  uri: string;
  shared: boolean;
}

/**
 * Copia el PDF recién generado por expo-print a un nombre legible dentro del mismo directorio de
 * caché. Es puramente cosmético (expo-print siempre genera un nombre temporal propio): si el
 * copy/rename falla por cualquier motivo (permisos, almacenamiento), se comparte igual el archivo
 * original sin nombre amigable — nunca lanza, nunca bloquea la generación ni afecta la compra.
 */
function renameToFriendlyName(sourceUri: string, fileName: string): string {
  try {
    const destination = new File(Paths.cache, fileName);
    if (destination.exists) {
      destination.delete();
    }
    new File(sourceUri).copy(destination);
    return destination.uri;
  } catch {
    return sourceUri;
  }
}

/**
 * Genera el PDF a partir del HTML ya armado y lo comparte con el selector nativo, igual que
 * utils/reportPdf.ts#generateAndShareReportPdf (mismo expo-print/expo-sharing, mismo manejo de
 * "sharing no disponible"). Se mantiene como función propia en vez de reutilizar esa exactamente
 * porque acá además hace falta el paso de renombrado a un nombre de archivo legible entre generar
 * y compartir, algo que el flujo de reportes no necesita.
 */
export async function generateAndSharePurchaseReceiptPdf(
  html: string,
  fileName: string
): Promise<GeneratePurchaseReceiptPdfResult> {
  const { uri } = await Print.printToFileAsync({ html });
  const finalUri = renameToFriendlyName(uri, fileName);

  const canShare = await Sharing.isAvailableAsync();
  if (!canShare) {
    return { uri: finalUri, shared: false };
  }

  await Sharing.shareAsync(finalUri, { mimeType: 'application/pdf', dialogTitle: fileName, UTI: 'com.adobe.pdf' });
  return { uri: finalUri, shared: true };
}
