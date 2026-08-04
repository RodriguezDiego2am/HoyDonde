import { escapeHtml } from './reportPdf';
import { formatPrecio } from './format';
import type { VentasReporteResponse } from '@/services/reportService';

/**
 * Cuerpo HTML del PDF de ventas simuladas (docs/api-mvp-plan.md §11, Parte 7): lectura rápida,
 * gráfico temporal y Top 5 en HTML/CSS puro (barras con `width`/`height` en porcentaje, sin
 * canvas ni JavaScript), desglose por categoría/tipo de entrada. Reutiliza escapeHtml de
 * utils/reportPdf.ts; el disclaimer de pagos simulados lo agrega wrapReportDocument.
 */

const PORC = (n: number) => `${n.toFixed(1)}%`;

function buildResumenTableHtml(resumen: VentasReporteResponse['resumen']): string {
  return `
  <div class="section-title">Lectura rápida</div>
  <table>
    <tbody>
      <tr><th>Importe emitido</th><td>${escapeHtml(formatPrecio(resumen.importeEmitido))}</td><th>Compras</th><td>${resumen.cantidadCompras}</td></tr>
      <tr><th>Entradas</th><td>${resumen.entradasEmitidas}</td><th>Compra promedio</th><td>${escapeHtml(formatPrecio(resumen.importePromedioPorCompra))}</td></tr>
      <tr><th>Precio promedio</th><td>${escapeHtml(formatPrecio(resumen.precioPromedioEntrada))}</td><th>Clientes únicos</th><td>${resumen.clientesUnicos}</td></tr>
      <tr><th>Evento destacado</th><td colspan="3">${resumen.eventoConMayorImporte ? escapeHtml(resumen.eventoConMayorImporte.eventoNombre) : '—'}</td></tr>
    </tbody>
  </table>`;
}

function buildSerieTemporalHtml(serie: VentasReporteResponse['serieTemporal']): string {
  if (serie.length === 0) {
    return '<div class="section-title">Evolución temporal</div><p class="empty">Sin datos para el período elegido.</p>';
  }

  const maxImporte = Math.max(0, ...serie.map((b) => b.importeEmitido));
  const columnas = serie
    .map((b) => {
      const alturaPorcentaje = maxImporte <= 0 ? 4 : Math.max(4, (b.importeEmitido / maxImporte) * 100);
      return `<div class="chart-col">
        <div class="chart-value">${b.importeEmitido > 0 ? escapeHtml(formatPrecio(b.importeEmitido)) : '—'}</div>
        <div class="chart-track"><div class="chart-bar-v" style="height:${alturaPorcentaje}%;"></div></div>
        <div class="chart-label">${escapeHtml(b.etiqueta)}</div>
      </div>`;
    })
    .join('');

  return `
  <div class="section-title">Evolución temporal</div>
  <div class="chart-row">${columnas}</div>`;
}

function buildTopEventosHtml(top: VentasReporteResponse['topEventos']): string {
  if (top.length === 0) {
    return '<div class="section-title">Top eventos por importe emitido</div><p class="empty">Sin eventos con ventas en el período elegido.</p>';
  }

  const maxImporte = Math.max(0, ...top.map((e) => e.importeEmitido));
  const filas = top
    .map((e) => {
      const anchoPorcentaje = maxImporte <= 0 ? 4 : Math.max(4, (e.importeEmitido / maxImporte) * 100);
      return `<div class="hbar-row">
        <div class="hbar-label">${escapeHtml(e.eventoNombre)}</div>
        <div class="hbar-track"><div class="hbar-fill" style="width:${anchoPorcentaje}%;"></div></div>
        <div class="hbar-value">${escapeHtml(formatPrecio(e.importeEmitido))} · ${e.entradasEmitidas} ${e.entradasEmitidas === 1 ? 'entrada' : 'entradas'}</div>
      </div>`;
    })
    .join('');

  return `<div class="section-title">Top eventos por importe emitido</div>${filas}`;
}

function buildPorCategoriaHtml(porCategoria: VentasReporteResponse['porCategoria']): string {
  if (porCategoria.length === 0) {
    return '<div class="section-title">Por categoría</div><p class="empty">Sin datos para el período elegido.</p>';
  }
  const filas = porCategoria
    .map((c) => `<tr><td>${escapeHtml(c.categoria)}</td><td>${c.cantidadCompras}</td><td>${c.entradasEmitidas}</td><td>${escapeHtml(formatPrecio(c.importeEmitido))}</td><td>${PORC(c.porcentajeDelImporteTotal)}</td></tr>`)
    .join('');
  return `
  <div class="section-title">Por categoría</div>
  <table>
    <thead><tr><th>Categoría</th><th>Compras</th><th>Entradas</th><th>Importe emitido</th><th>% del total</th></tr></thead>
    <tbody>${filas}</tbody>
  </table>`;
}

function buildPorTipoEntradaHtml(porTipo: VentasReporteResponse['porTipoEntrada']): string {
  if (porTipo.length === 0) return '';
  const filas = porTipo
    .map((t) => `<tr><td>${escapeHtml(t.ticketTypeNombre)}</td><td>${t.cantidadComprasDistintas}</td><td>${t.entradasEmitidas}</td><td>${escapeHtml(formatPrecio(t.importeEmitido))}</td><td>${PORC(t.porcentajeDelImporteTotal)}</td></tr>`)
    .join('');
  return `
  <div class="section-title">Por tipo de entrada</div>
  <table>
    <thead><tr><th>Tipo</th><th>Compras</th><th>Entradas</th><th>Importe emitido</th><th>% del total</th></tr></thead>
    <tbody>${filas}</tbody>
  </table>`;
}

const CHART_CSS = `
  .chart-row { display: flex; align-items: flex-end; gap: 6px; margin-bottom: 20px; page-break-inside: avoid; }
  .chart-col { display: flex; flex-direction: column; align-items: center; width: 40px; }
  .chart-value { font-size: 8px; color: #6B6357; margin-bottom: 2px; text-align: center; }
  .chart-track { height: 90px; width: 24px; display: flex; align-items: flex-end; border-bottom: 1px solid #171512; }
  .chart-bar-v { width: 100%; background: #F04E3E; border: 1px solid #171512; border-bottom: none; min-height: 2px; }
  .chart-label { font-size: 8px; font-weight: bold; margin-top: 4px; text-align: center; }
  .hbar-row { margin-bottom: 12px; page-break-inside: avoid; }
  .hbar-label { font-size: 12px; font-weight: bold; margin-bottom: 3px; }
  .hbar-track { height: 12px; border: 1px solid #171512; background: #F3EBDD; }
  .hbar-fill { height: 100%; background: #F04E3E; }
  .hbar-value { font-size: 10px; color: #6B6357; margin-top: 2px; }
`;

export function buildVentasBodyHtml(report: VentasReporteResponse): string {
  return `<style>${CHART_CSS}</style>
    ${buildResumenTableHtml(report.resumen)}
    ${buildSerieTemporalHtml(report.serieTemporal)}
    ${buildTopEventosHtml(report.topEventos)}
    ${buildPorCategoriaHtml(report.porCategoria)}
    ${buildPorTipoEntradaHtml(report.porTipoEntrada)}`;
}
