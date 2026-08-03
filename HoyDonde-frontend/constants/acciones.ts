/**
 * Códigos de Accion usados por la navegación y los gates de UI (espejo parcial de
 * HoyDonde.API/Authorization/Acciones.cs — única fuente de verdad real). Nunca un
 * mapa rol → acción: cada pantalla decide su visibilidad con AuthContext.hasAccion
 * contra uno de estos códigos, nunca contra un nombre de rol.
 */
export const ACCIONES = {
  USUARIO_CREAR_ORGANIZADOR: 'USUARIO_CREAR_ORGANIZADOR',
  CONTROL_CREAR: 'CONTROL_CREAR',
  EVENTO_CREAR: 'EVENTO_CREAR',
  EVENTO_EDITAR_PROPIO: 'EVENTO_EDITAR_PROPIO',
  EVENTO_PUBLICAR_PROPIO: 'EVENTO_PUBLICAR_PROPIO',
  EVENTO_CANCELAR_PROPIO: 'EVENTO_CANCELAR_PROPIO',
  EVENTO_VER_PROPIOS: 'EVENTO_VER_PROPIOS',
  TICKET_VER_PROPIO: 'TICKET_VER_PROPIO',
  TICKET_VALIDAR: 'TICKET_VALIDAR',
} as const;
