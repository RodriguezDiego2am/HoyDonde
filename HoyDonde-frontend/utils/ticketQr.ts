/**
 * Payload del QR de un ticket: JSON compacto con exactamente `ticketId` y
 * `eventId`, sin firma ni datos derivados (el backend no implementa QR firmado
 * ni validación offline — docs/api-mvp-plan.md §"Frontend 1", punto 5).
 * Forma compartida para que Frontend 3 (Control) reutilice el parser al leer
 * el QR escaneado.
 */
export interface TicketQrPayload {
  ticketId: string;
  eventId: string;
}

export function buildTicketQrPayload(ticketId: string, eventId: string): string {
  const payload: TicketQrPayload = { ticketId, eventId };
  return JSON.stringify(payload);
}

export function parseTicketQrPayload(raw: string): TicketQrPayload | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  if (
    typeof parsed !== 'object' ||
    parsed === null ||
    typeof (parsed as Record<string, unknown>).ticketId !== 'string' ||
    typeof (parsed as Record<string, unknown>).eventId !== 'string' ||
    (parsed as Record<string, unknown>).ticketId === '' ||
    (parsed as Record<string, unknown>).eventId === ''
  ) {
    return null;
  }

  return { ticketId: (parsed as TicketQrPayload).ticketId, eventId: (parsed as TicketQrPayload).eventId };
}
