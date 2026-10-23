import { buildTicketQrValue, parseTicketQrToken } from './ticketQr';

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUv';

describe('ticketQr', () => {
  it('renders the backend token verbatim as the QR value (no JSON, no ids)', () => {
    expect(buildTicketQrValue(TOKEN)).toBe(TOKEN);
  });

  it('has no QR value when the ticket has no token (not owner, not usable, or not migrated)', () => {
    expect(buildTicketQrValue(null)).toBeNull();
    expect(buildTicketQrValue(undefined)).toBeNull();
    expect(buildTicketQrValue('')).toBeNull();
  });

  it('round-trips: what the QR encodes is exactly what the scanner parses', () => {
    expect(parseTicketQrToken(buildTicketQrValue(TOKEN) as string)).toBe(TOKEN);
  });

  it('accepts url-safe base64 characters and trims surrounding whitespace', () => {
    expect(parseTicketQrToken('  abc-_DEFghiJKLmnoPQR12  ')).toBe('abc-_DEFghiJKLmnoPQR12');
  });

  it('rejects the legacy { ticketId, eventId } JSON payload', () => {
    expect(parseTicketQrToken(JSON.stringify({ ticketId: 'ticket-1', eventId: 'evento-1' }))).toBeNull();
  });

  it('rejects wrong lengths, foreign characters and URLs', () => {
    expect(parseTicketQrToken('short')).toBeNull();
    expect(parseTicketQrToken(`${TOKEN}x`)).toBeNull();
    expect(parseTicketQrToken('AbCdEfGhIjKlMnOpQrSt+/')).toBeNull();
    expect(parseTicketQrToken('https://example.com/not-a-ticket')).toBeNull();
    expect(parseTicketQrToken('')).toBeNull();
  });
});
