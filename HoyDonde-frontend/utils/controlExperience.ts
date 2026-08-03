import { ACCIONES } from '@/constants/acciones';

/**
 * Un Control exclusivo es una cuenta cuyas acciones efectivas son únicamente TICKET_VALIDAR
 * (nunca por nombre de rol: la decisión se toma sobre el arreglo de acciones que ya devuelve
 * /api/auth/sync). Un usuario multirol que además tenga otras acciones (p. ej. también
 * Organizador) conserva la navegación general y solo entra a Control desde su Panel.
 */
export function isControlExclusivo(acciones: readonly string[]): boolean {
  return acciones.length > 0 && acciones.every((accion) => accion === ACCIONES.TICKET_VALIDAR);
}
