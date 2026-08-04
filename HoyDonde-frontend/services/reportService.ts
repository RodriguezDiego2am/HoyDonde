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
  // null/no aplicable salvo que el evento esté efectivamente Finalizado (nunca "ausentismo" de un
  // evento futuro o en curso).
  entradasNoUtilizadas: number | null;
  porcentajeNoUtilizacion: number | null;
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
  // Agregado exclusivamente sobre los eventos efectivamente Finalizados del conjunto.
  entradasNoUtilizadasFinalizados: number;
  porcentajeNoUtilizacionFinalizados: number;
}

/** Espejo de ReporteEventoDestacadoPorcentajeDto (evento destacado por un porcentaje). */
export interface ReporteEventoDestacadoPorcentaje {
  eventId: string;
  nombre: string;
  porcentaje: number;
}

/** Espejo de ReporteEventoDestacadoImporteDto (evento destacado por importe emitido). */
export interface ReporteEventoDestacadoImporte {
  eventId: string;
  nombre: string;
  importeEmitido: number;
}

/** Espejo de ReporteTopEventoDto (fila del ranking Top 5 del reporte de desempeño). */
export interface ReporteTopEvento {
  eventId: string;
  nombre: string;
  importeEmitido: number;
  entradasEmitidas: number;
}

/** Espejo de ReporteDestacadosDto: null/vacío únicamente cuando no hay ningún evento en el conjunto filtrado. */
export interface ReporteDestacados {
  eventoMayorOcupacion: ReporteEventoDestacadoPorcentaje | null;
  eventoMayorAsistencia: ReporteEventoDestacadoPorcentaje | null;
  eventoMayorImporte: ReporteEventoDestacadoImporte | null;
  top5PorImporte: ReporteTopEvento[];
}

/** Espejo de ReporteEventosResponseDto. */
export interface ReporteEventosResponse {
  fechaDesde: string;
  fechaHasta: string;
  aclaracionImporte: string;
  resumen: ReporteResumen;
  destacados: ReporteDestacados;
  eventos: ReporteEventoDetalle[];
}

/** Espejo de ReporteAdminEventosResponseDto. */
export interface ReporteAdminEventosResponse {
  fechaDesde: string;
  fechaHasta: string;
  aclaracionImporte: string;
  resumen: ReporteResumen;
  destacados: ReporteDestacados;
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

// ============================================================================
// Ventas simuladas (docs/api-mvp-plan.md §11): filtra por Compra.FechaCompra -cuándo se vendió-,
// nunca por Event.FechaInicio -cuándo ocurre el evento, eso es el reporte de desempeño de arriba-.
// ============================================================================

/** Espejo de VentasOrganizerFilterDto. Nunca lleva organizadorPersonaId: sale del token en el backend. */
export interface VentasOrganizerFilter {
  fechaDesde: string;
  fechaHasta: string;
  eventId?: string;
  categoria?: string;
  ticketTypeId?: string;
}

/** Espejo de VentasAdminFilterDto. Sin ticketTypeId (no forma parte de los filtros del Administrador). */
export interface VentasAdminFilter {
  fechaDesde: string;
  fechaHasta: string;
  organizadorPersonaId?: string;
  eventId?: string;
  categoria?: string;
}

/** Espejo de VentasEventoDestacadoDto. */
export interface VentasEventoDestacado {
  eventoId: string;
  eventoNombre: string;
  importeEmitido: number;
  entradasEmitidas: number;
}

/** Espejo de VentasResumenDto. División por cero -> 0. Nunca expone ClientePersonaId. */
export interface VentasResumen {
  cantidadCompras: number;
  entradasEmitidas: number;
  importeEmitido: number;
  importePromedioPorCompra: number;
  precioPromedioEntrada: number;
  clientesUnicos: number;
  eventoConMayorImporte: VentasEventoDestacado | null;
  eventoConMasEntradas: VentasEventoDestacado | null;
}

/** Espejo de VentasSerieBucketDto: un período (día/semana/mes) de la serie temporal, en horario Argentina. */
export interface VentasSerieBucket {
  periodoDesde: string;
  periodoHasta: string;
  etiqueta: string;
  cantidadCompras: number;
  entradasEmitidas: number;
  importeEmitido: number;
}

/** Espejo de VentasTopEventoDto (máximo 5 filas, orden determinístico). */
export interface VentasTopEvento {
  eventoId: string;
  eventoNombre: string;
  cantidadCompras: number;
  entradasEmitidas: number;
  importeEmitido: number;
  importePromedioCompra: number;
}

/** Espejo de VentasCategoriaDto. "Sin categoría" es una Compra legacy sin fotografía de Categoria. */
export interface VentasCategoria {
  categoria: string;
  cantidadCompras: number;
  entradasEmitidas: number;
  importeEmitido: number;
  porcentajeDelImporteTotal: number;
}

/** Espejo de VentasTicketTypeDto. Vacío salvo que el filtro traiga un eventId. */
export interface VentasTicketType {
  ticketTypeId: string;
  ticketTypeNombre: string;
  cantidadComprasDistintas: number;
  entradasEmitidas: number;
  importeEmitido: number;
  porcentajeDelImporteTotal: number;
}

/** Espejo de VentasEventoOpcionDto: opción mínima (sin métricas) para poblar un selector. */
export interface VentasEventoOpcion {
  id: string;
  nombre: string;
}

/** Espejo de VentasTicketTypeOpcionDto: opción mínima (sin métricas) para poblar un selector. */
export interface VentasTicketTypeOpcion {
  id: string;
  nombre: string;
}

/**
 * Espejo de VentasFiltrosDisponiblesDto: opciones reales para los selectores de evento/tipo de
 * entrada — a diferencia de `topEventos` (máximo 5), `eventos` incluye todos los eventos con
 * Compras en el conjunto ya filtrado por rango/ownership/organizador/categoría, calculado ANTES de
 * aplicar `eventId` (así sigue completo aunque ya haya un evento seleccionado). `tiposEntrada`
 * solo se puebla cuando el filtro trae `eventId`; en el reporte del Administrador siempre queda
 * vacío (su contrato no tiene `ticketTypeId`).
 */
export interface VentasFiltrosDisponibles {
  eventos: VentasEventoOpcion[];
  tiposEntrada: VentasTicketTypeOpcion[];
}

/** Espejo de VentasReporteResponseDto: mismo shape para el reporte del Organizador y del Administrador. */
export interface VentasReporteResponse {
  fechaDesde: string;
  fechaHasta: string;
  aclaracionImporte: string;
  resumen: VentasResumen;
  serieTemporal: VentasSerieBucket[];
  topEventos: VentasTopEvento[];
  porCategoria: VentasCategoria[];
  porTipoEntrada: VentasTicketType[];
  filtrosDisponibles: VentasFiltrosDisponibles;
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

  /** GET /api/reports/organizer/sales — Policy REPORTE_VER_PROPIO. Rango sobre Compra.FechaCompra. */
  getOrganizerSalesReport: async (filter: VentasOrganizerFilter): Promise<VentasReporteResponse> => {
    const response = await apiClient.get<VentasReporteResponse>('/reports/organizer/sales', { params: filter });
    return response.data;
  },

  /** GET /api/reports/admin/sales — Policy REPORTE_VER_GLOBAL. Rango sobre Compra.FechaCompra. */
  getAdminSalesReport: async (filter: VentasAdminFilter): Promise<VentasReporteResponse> => {
    const response = await apiClient.get<VentasReporteResponse>('/reports/admin/sales', { params: filter });
    return response.data;
  },
};
