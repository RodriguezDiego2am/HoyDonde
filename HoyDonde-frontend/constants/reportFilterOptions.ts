import { ChipOption } from '@/components/reports/ChipSelectRow';
import { ReporteEventoEstado, SecurityAuditTargetTipo } from '@/services/reportService';

/** Categorías reales del enum Event.EventCategory (HoyDonde.API/Models/Event.cs), usadas por los filtros de los reportes de eventos (Organizador/Admin). */
export const REPORT_CATEGORIAS: ChipOption<string>[] = [
  { value: 'Musica', label: 'Música' },
  { value: 'Deportes', label: 'Deportes' },
  { value: 'Tecnologia', label: 'Tecnología' },
  { value: 'Arte', label: 'Arte' },
  { value: 'Otros', label: 'Otros' },
];

/** Estados efectivos (Event.EventEffectiveStatus): incluye "Finalizado", que nunca se persiste pero sí se filtra en el reporte. */
export const REPORT_ESTADOS: ChipOption<ReporteEventoEstado>[] = [
  { value: 'Borrador', label: 'Borrador' },
  { value: 'Publicado', label: 'Publicado' },
  { value: 'Cancelado', label: 'Cancelado' },
  { value: 'Finalizado', label: 'Finalizado' },
];

/** Operaciones reales escritas por SecurityAdminService.NuevoAudit (HoyDonde.API/Services/SecurityAdminService.cs) — nunca un valor inventado. */
export const REPORT_OPERACIONES_AUDITORIA: ChipOption<string>[] = [
  { value: 'ROL_CREAR', label: 'Rol creado' },
  { value: 'ROL_EDITAR', label: 'Rol editado' },
  { value: 'ROL_ACTIVAR', label: 'Rol activado' },
  { value: 'ROL_DESACTIVAR', label: 'Rol desactivado' },
  { value: 'ROL_ASIGNAR_ACCION', label: 'Acción asignada a rol' },
  { value: 'ROL_QUITAR_ACCION', label: 'Acción quitada de rol' },
  { value: 'USUARIO_ASIGNAR_ROL', label: 'Rol asignado a usuario' },
  { value: 'USUARIO_QUITAR_ROL', label: 'Rol quitado de usuario' },
  { value: 'USUARIO_ACTIVAR', label: 'Usuario activado' },
  { value: 'USUARIO_DESACTIVAR', label: 'Usuario desactivado' },
];

/** Espejo de SecurityAuditTargetTipo — el cuarto valor real "UsuarioRol" incluido junto a los tres del plan original (ver reportService.ts). */
export const REPORT_TARGET_TIPOS: ChipOption<SecurityAuditTargetTipo>[] = [
  { value: 'Rol', label: 'Rol' },
  { value: 'Usuario', label: 'Usuario' },
  { value: 'RolAccion', label: 'Rol ↔ Acción' },
  { value: 'UsuarioRol', label: 'Usuario ↔ Rol' },
];
