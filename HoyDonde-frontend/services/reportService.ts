import { apiClient } from './APIService';

/** Espejo de ReporteTicketTypeDetalleDto (HoyDonde.API/DTOs). CapacidadInicial es siempre una derivación, nunca un dato persistido. */
export interface ReporteTicketTypeDetalle {
  ticketTypeId: string;
  nombre: string;
  capacidadInicial: number;
  stockDisponible: number;
  entradasEmitidas: number;
  entradasUsadas: number;
  entradasAnuladas: number;
  entradasPendientes: number;
  porcentajeOcupacion: number;
  porcentajeAsistencia: number;
  porcentajeUtilizacion: number;
  importeEmitido: number;
}

/** Espejo de ReporteEventoDetalleDto (reporte del Organizador, GET /reports/organizer/events). */
export interface ReporteEventoDetalle {
  eventId: string;
  nombre: string;
  ubicacion: string;
  categoria: string;
  estado: string;
  fechaInicio: string;
  fechaFin: string;
  capacidadInicial: number;
  stockDisponible: number;
  entradasEmitidas: number;
  entradasUsadas: number;
  entradasAnuladas: number;
  entradasPendientes: number;
  porcentajeOcupacion: number;
  porcentajeAsistencia: number;
  porcentajeUtilizacion: number;
  importeEmitido: number;
  tiposDeEntrada: ReporteTicketTypeDetalle[];
}

/** Espejo de ReporteAdminEventoDetalleDto: mismo shape + organizadorPersonaId (el Admin ve eventos de cualquier organizador). */
export interface ReporteAdminEventoDetalle extends ReporteEventoDetalle {
  organizadorPersonaId: string;
}

/** Espejo de ReporteResumenDto: agregado sobre todo el conjunto del reporte. */
export interface ReporteResumen {
  cantidadEventos: number;
  capacidadInicial: number;
  stockDisponible: number;
  entradasEmitidas: number;
  entradasUsadas: number;
  entradasAnuladas: number;
  entradasPendientes: number;
  porcentajeOcupacion: number;
  porcentajeAsistencia: number;
  porcentajeUtilizacion: number;
  importeEmitido: number;
}

/** Espejo de ReporteEventosResponseDto. */
export interface ReporteEventosResponse {
  fechaDesde: string;
  fechaHasta: string;
  aclaracionImporte: string;
  resumen: ReporteResumen;
  eventos: ReporteEventoDetalle[];
}

/** Espejo de ReporteAdminEventosResponseDto. */
export interface ReporteAdminEventosResponse {
  fechaDesde: string;
  fechaHasta: string;
  aclaracionImporte: string;
  resumen: ReporteResumen;
  eventos: ReporteAdminEventoDetalle[];
}

/** Espejo de Event.EventEffectiveStatus — únicos valores que el filtro de estado acepta. */
export type ReporteEventoEstado = 'Borrador' | 'Publicado' | 'Cancelado' | 'Finalizado';

/**
 * Filtro de GET /reports/organizer/events (espejo de ReporteEventosFilterDto). fechaDesde/
 * fechaHasta viajan en UTC (ISO 8601, mismo criterio que EventSearchFilter/utils/datetime.ts).
 * Nunca lleva organizadorPersonaId: el organizador sale siempre del token en el backend.
 */
export interface ReporteOrganizerFilter {
  fechaDesde: string;
  fechaHasta: string;
  estado?: ReporteEventoEstado;
  categoria?: string;
  eventId?: string;
  ticketTypeId?: string;
}

/** Espejo de ReporteAdminEventosFilterDto — sin eventId/ticketTypeId, con organizadorPersonaId opcional y arbitrario. */
export interface ReporteAdminFilter {
  fechaDesde: string;
  fechaHasta: string;
  estado?: ReporteEventoEstado;
  categoria?: string;
  organizadorPersonaId?: string;
}

/**
 * Espejo de SecurityAuditTargetTipo (HoyDonde.API/DTOs/SecurityAuditReportFilterDto.cs).
 * "UsuarioRol" es un cuarto valor real (asignar/quitar rol a un usuario) no documentado en el
 * plan original, que solo enumeraba Rol/Usuario/RolAccion — se incluye porque ya es el valor que
 * el backend persiste para esa operación (ver riesgos/desviaciones del cierre del módulo).
 */
export type SecurityAuditTargetTipo = 'Rol' | 'Usuario' | 'RolAccion' | 'UsuarioRol';

/** Espejo de SecurityAuditReportFilterDto. Todo opcional: sin fechaDesde/fechaHasta, el backend aplica el default de 30 días. */
export interface SecurityAuditFilter {
  fechaDesde?: string;
  fechaHasta?: string;
  operacion?: string;
  actorUsuarioId?: string;
  targetTipo?: SecurityAuditTargetTipo;
  targetId?: string;
}

/** Espejo de SecurityAuditReporteDto. actorEmail es null si el Usuario actor ya no existe. */
export interface SecurityAuditReporteItem {
  timestamp: string;
  operacion: string;
  actorUsuarioId: string;
  actorEmail: string | null;
  targetTipo: string;
  targetId: string;
  detalle: string;
}

/** Espejo de SecurityAuditReporteResponseDto. fechaDesde/fechaHasta son siempre el rango efectivo (incluye el default). */
export interface SecurityAuditReporteResponse {
  fechaDesde: string;
  fechaHasta: string;
  auditorias: SecurityAuditReporteItem[];
}

/**
 * Módulo de reportes (`/api/reports`, docs/api-mvp-plan.md §11, API_Documentation.md §11). Todo
 * de solo lectura: ningún método de acá muta estado. El actor (Organizador/Administrador) siempre
 * se resuelve en el backend desde el token o desde la policy, nunca se manda en el body/query.
 */
export const reportService = {
  /** GET /api/reports/organizer/events — Policy REPORTE_VER_PROPIO. */
  getOrganizerEventsReport: async (filter: ReporteOrganizerFilter): Promise<ReporteEventosResponse> => {
    const response = await apiClient.get<ReporteEventosResponse>('/reports/organizer/events', { params: filter });
    return response.data;
  },

  /** GET /api/reports/admin/events — Policy REPORTE_VER_GLOBAL. */
  getAdminEventsReport: async (filter: ReporteAdminFilter): Promise<ReporteAdminEventosResponse> => {
    const response = await apiClient.get<ReporteAdminEventosResponse>('/reports/admin/events', { params: filter });
    return response.data;
  },

  /** GET /api/reports/admin/security-audits — Policy REPORTE_VER_GLOBAL. */
  getSecurityAuditsReport: async (filter: SecurityAuditFilter): Promise<SecurityAuditReporteResponse> => {
    const response = await apiClient.get<SecurityAuditReporteResponse>('/reports/admin/security-audits', { params: filter });
    return response.data;
  },
};
