import * as Print from 'expo-print';
import * as Sharing from 'expo-sharing';

/**
 * Generación de PDF de reportes en el propio frontend (docs/api-mvp-plan.md §11.6): la API
 * siempre devuelve JSON puro, nunca un PDF generado en el backend. El HTML se arma acá mismo con
 * los datos ya recibidos -nunca una plantilla de terceros-, pero igual se escapa todo texto
 * dinámico porque nombres de evento/organizador/email vienen de datos de usuario.
 */

/** Escapa entidades HTML básicas. Usar SIEMPRE antes de interpolar un valor dinámico en el HTML del reporte. */
export function escapeHtml(value: string | number | null | undefined): string {
  const str = value === null || value === undefined ? '' : String(value);
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

export interface ReportFiltroActivo {
  label: string;
  value: string;
}

const DISCLAIMER_PAGOS_SIMULADOS =
  'El MVP no procesa pagos reales: "importe emitido" es la suma de los precios fotografiados en cada ticket al comprar, nunca una recaudación ni un cobro real.';

/**
 * Envoltorio HTML compartido por los tres reportes (identidad "Cartelera urbana" — CLAUDE.md
 * §6): título, alcance/filtros activos en texto legible, fecha de generación, el cuerpo
 * específico del reporte (tabla de resumen/desglose o de auditorías) y la aclaración fija de
 * pagos simulados. Los tres reportes comparten esta cáscara para no duplicar CSS/estructura, sin
 * convertirse en un motor genérico: cada reporte arma su propio `bodyHtml`.
 */
export function wrapReportDocument(opts: {
  eyebrow: string;
  title: string;
  periodoLabel: string;
  filtros: ReportFiltroActivo[];
  bodyHtml: string;
  disclaimer?: string;
}): string {
  const generadoEl = new Date().toLocaleString('es-AR');
  const filtrosHtml = opts.filtros.length
    ? opts.filtros
        .map((f) => `<span class="chip"><strong>${escapeHtml(f.label)}:</strong> ${escapeHtml(f.value)}</span>`)
        .join('')
    : '<span class="chip chip-muted">Sin filtros aplicados</span>';

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
  .eyebrow {
    font-size: 11px;
    letter-spacing: 1.5px;
    text-transform: uppercase;
    color: #6B6357;
    font-weight: bold;
  }
  h1 {
    font-size: 26px;
    margin: 4px 0 2px;
    border-bottom: 3px solid #171512;
    padding-bottom: 12px;
  }
  .periodo {
    font-size: 13px;
    color: #6B6357;
    margin-bottom: 4px;
  }
  .generado {
    font-size: 11px;
    color: #6B6357;
    margin-bottom: 16px;
  }
  .chips { margin-bottom: 20px; }
  .chip {
    display: inline-block;
    border: 1px solid #171512;
    border-radius: 2px;
    padding: 3px 8px;
    margin: 0 6px 6px 0;
    font-size: 11px;
  }
  .chip-muted { color: #6B6357; border-color: #D8CDBB; }
  table {
    width: 100%;
    border-collapse: collapse;
    margin-bottom: 20px;
    font-size: 12px;
  }
  th, td {
    border: 1px solid #D8CDBB;
    padding: 6px 8px;
    text-align: left;
  }
  th {
    background: #D8CDBB;
    text-transform: uppercase;
    font-size: 10px;
    letter-spacing: 0.5px;
  }
  .section-title {
    font-size: 15px;
    font-weight: bold;
    margin: 20px 0 8px;
    border-bottom: 1px solid #171512;
    padding-bottom: 4px;
  }
  .disclaimer {
    margin-top: 24px;
    padding: 12px;
    border: 1px dashed #6B6357;
    font-size: 11px;
    color: #6B6357;
    font-style: italic;
  }
  .empty {
    font-size: 12px;
    color: #6B6357;
    font-style: italic;
  }
</style>
</head>
<body>
  <div class="eyebrow">${escapeHtml(opts.eyebrow)}</div>
  <h1>${escapeHtml(opts.title)}</h1>
  <div class="periodo">${escapeHtml(opts.periodoLabel)}</div>
  <div class="generado">Generado el ${escapeHtml(generadoEl)}</div>
  <div class="chips">${filtrosHtml}</div>
  ${opts.bodyHtml}
  <div class="disclaimer">${escapeHtml(opts.disclaimer ?? DISCLAIMER_PAGOS_SIMULADOS)}</div>
</body>
</html>`;
}

export interface GenerateReportPdfResult {
  uri: string;
  shared: boolean;
}

/**
 * Genera el PDF a partir del HTML ya armado y lo comparte con el selector nativo
 * (expo-sharing). Si el dispositivo no tiene sharing disponible (p. ej. algunos emuladores/Web),
 * devuelve shared:false con el uri del archivo ya generado en caché local -la pantalla que llama
 * decide qué mensaje mostrar, esta función nunca lanza en ese caso-.
 */
export async function generateAndShareReportPdf(html: string, dialogTitle: string): Promise<GenerateReportPdfResult> {
  const { uri } = await Print.printToFileAsync({ html });

  const canShare = await Sharing.isAvailableAsync();
  if (!canShare) {
    return { uri, shared: false };
  }

  await Sharing.shareAsync(uri, { mimeType: 'application/pdf', dialogTitle, UTI: 'com.adobe.pdf' });
  return { uri, shared: true };
}
