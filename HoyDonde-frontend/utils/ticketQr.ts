/**
 * The QR of a ticket encodes ONLY the opaque token issued by the backend (128-bit random,
 * URL-safe base64, 22 characters). It is not a JSON payload and carries no ticket or event id:
 * the backend resolves everything from the token, and rotates it when the ticket is transferred,
 * so a stale QR (or a screenshot of it) stops validating. The legacy `{ticketId,eventId}` payload
 * is no longer accepted by the door validation.
 */
export const QR_TOKEN_LENGTH = 22;

const QR_TOKEN_PATTERN = /^[A-Za-z0-9_-]{22}$/;

/** Value to render in the QR image, or null when the ticket has no usable token. */
export function buildTicketQrValue(qrToken: string | null | undefined): string | null {
  if (typeof qrToken !== 'string') return null;
  const trimmed = qrToken.trim();
  return QR_TOKEN_PATTERN.test(trimmed) ? trimmed : null;
}

/**
 * Parses what the scanner (or manual entry) read. Returns the token, or null when the content is
 * not a ticket token — including legacy JSON payloads. Only the shape is checked here; whether
 * the token is current is always decided by the API.
 */
export function parseTicketQrToken(raw: string): string | null {
  return buildTicketQrValue(raw);
}
