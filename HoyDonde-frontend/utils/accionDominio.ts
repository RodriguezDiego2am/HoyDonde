/**
 * Agrupación puramente visual de códigos de Accion por dominio (prefijo antes del primer "_").
 * Nunca es un mapa rol→acción: solo decide en qué sección del detalle de un Rol aparece cada
 * chip. La autorización real sigue viviendo exclusivamente en el backend.
 */
const DOMINIO_LABELS: Record<string, string> = {
  USUARIO: 'Usuario',
  ROL: 'Rol',
  EVENTO: 'Evento',
  TICKET: 'Ticket',
  CONTROL: 'Control',
};

export function dominioDeAccion(codigo: string): string {
  const prefijo = codigo.split('_')[0];
  return DOMINIO_LABELS[prefijo] ?? 'Otras';
}

/** Orden estable de dominios para no reordenar la UI en cada render. */
export const ORDEN_DOMINIOS = ['Usuario', 'Rol', 'Evento', 'Ticket', 'Control', 'Otras'];
