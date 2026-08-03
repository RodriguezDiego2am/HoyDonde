import { escapeHtml } from './reportPdf';
import { formatFechaHora, formatPrecio } from './format';
import type {
  ReporteEventoDetalle,
  ReporteAdminEventoDetalle,
  ReporteResumen,
  SecurityAuditReporteItem,
} from '@/services/reportService';

/**
 * Cuerpos de tabla específicos de cada reporte, reutilizados por el reporte del Organizador y el
 * reporte global del Administrador (mismo shape de ReporteResumen/ReporteEventoDetalle) — separado
 * de utils/reportPdf.ts (que es genérico: escape/cáscara HTML/generación) para no acoplar ese
 * archivo a los tipos de dominio del módulo de reportes.
 */

const PORC = (n: number) => `${n.toFixed(1)}%`;

export function buildResumenTableHtml(resumen: ReporteResumen): string {
  return `
  <div class="section-title">Resumen</div>
  <table>
    <tbody>
      <tr><th>Eventos</th><td>${resumen.cantidadEventos}</td><th>Capacidad inicial</th><td>${resumen.capacidadInicial}</td></tr>
      <tr><th>Entradas emitidas</th><td>${resumen.entradasEmitidas}</td><th>Stock disponible</th><td>${resumen.stockDisponible}</td></tr>
      <tr><th>Entradas usadas</th><td>${resumen.entradasUsadas}</td><th>Entradas pendientes</th><td>${resumen.entradasPendientes}</td></tr>
      <tr><th>Entradas anuladas</th><td>${resumen.entradasAnuladas}</td><th>Importe emitido</th><td>${escapeHtml(formatPrecio(resumen.importeEmitido))}</td></tr>
      <tr><th>% Ocupación</th><td>${PORC(resumen.porcentajeOcupacion)}</td><th>% Asistencia</th><td>${PORC(resumen.porcentajeAsistencia)}</td></tr>
      <tr><th>% Utilización</th><td>${PORC(resumen.porcentajeUtilizacion)}</td><td></td><td></td></tr>
    </tbody>
  </table>`;
}

export function buildEventosSectionHtml(
  eventos: (ReporteEventoDetalle | ReporteAdminEventoDetalle)[],
  opts?: { organizadorNombrePorPersonaId?: Record<string, string> }
): string {
  if (eventos.length === 0) {
    return '<div class="section-title">Eventos</div><p class="empty">Ningún evento coincide con los filtros aplicados.</p>';
  }

  const showOrganizador = eventos.some((e) => 'organizadorPersonaId' in e);

  const filas = eventos
    .map((e) => {
      const organizadorCell = showOrganizador
        ? `<td>${escapeHtml(
            opts?.organizadorNombrePorPersonaId?.[(e as ReporteAdminEventoDetalle).organizadorPersonaId] ??
              (e as ReporteAdminEventoDetalle).organizadorPersonaId
          )}</td>`
        : '';
      return `<tr>
        <td>${escapeHtml(e.nombre)}</td>
        ${organizadorCell}
        <td>${escapeHtml(e.categoria)}</td>
        <td>${escapeHtml(e.estado)}</td>
        <td>${escapeHtml(formatFechaHora(e.fechaInicio))}</td>
        <td>${e.entradasEmitidas}</td>
        <td>${e.entradasUsadas}</td>
        <td>${e.entradasPendientes}</td>
        <td>${PORC(e.porcentajeOcupacion)}</td>
        <td>${PORC(e.porcentajeAsistencia)}</td>
        <td>${escapeHtml(formatPrecio(e.importeEmitido))}</td>
      </tr>`;
    })
    .join('');

  const organizadorHeader = showOrganizador ? '<th>Organizador</th>' : '';

  const tiposDeEntradaSecciones = eventos
    .filter((e) => e.tiposDeEntrada.length > 0)
    .map(
      (e) => `
    <div class="section-title" style="font-size:13px;">${escapeHtml(e.nombre)} — tipos de entrada</div>
    <table>
      <thead>
        <tr><th>Tipo</th><th>Capacidad</th><th>Stock</th><th>Emitidas</th><th>Usadas</th><th>Pendientes</th><th>Importe emitido</th></tr>
      </thead>
      <tbody>
        ${e.tiposDeEntrada
          .map(
            (t) => `<tr>
              <td>${escapeHtml(t.nombre)}</td>
              <td>${t.capacidadInicial}</td>
              <td>${t.stockDisponible}</td>
              <td>${t.entradasEmitidas}</td>
              <td>${t.entradasUsadas}</td>
              <td>${t.entradasPendientes}</td>
              <td>${escapeHtml(formatPrecio(t.importeEmitido))}</td>
            </tr>`
          )
          .join('')}
      </tbody>
    </table>`
    )
    .join('');

  return `
  <div class="section-title">Eventos</div>
  <table>
    <thead>
      <tr>
        <th>Evento</th>
        ${organizadorHeader}
        <th>Categoría</th>
        <th>Estado</th>
        <th>Inicio</th>
        <th>Emitidas</th>
        <th>Usadas</th>
        <th>Pendientes</th>
        <th>% Ocup.</th>
        <th>% Asist.</th>
        <th>Importe emitido</th>
      </tr>
    </thead>
    <tbody>${filas}</tbody>
  </table>
  ${tiposDeEntradaSecciones}`;
}

export function buildSecurityAuditSectionHtml(auditorias: SecurityAuditReporteItem[]): string {
  if (auditorias.length === 0) {
    return '<div class="section-title">Auditorías</div><p class="empty">Ninguna auditoría coincide con los filtros aplicados.</p>';
  }

  const filas = auditorias
    .map(
      (a) => `<tr>
        <td>${escapeHtml(formatFechaHora(a.timestamp))}</td>
        <td>${escapeHtml(a.operacion)}</td>
        <td>${escapeHtml(a.actorEmail ?? a.actorUsuarioId)}</td>
        <td>${escapeHtml(a.targetTipo)}</td>
        <td>${escapeHtml(a.targetId)}</td>
        <td>${escapeHtml(a.detalle)}</td>
      </tr>`
    )
    .join('');

  return `
  <div class="section-title">Auditorías (${auditorias.length})</div>
  <table>
    <thead>
      <tr><th>Fecha</th><th>Operación</th><th>Actor</th><th>Objetivo</th><th>Id objetivo</th><th>Detalle</th></tr>
    </thead>
    <tbody>${filas}</tbody>
  </table>`;
}
