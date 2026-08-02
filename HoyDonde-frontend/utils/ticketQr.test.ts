import { buildTicketQrPayload, parseTicketQrPayload } from './ticketQr';

describe('ticketQr', () => {
  it('builds a compact JSON payload with exactly ticketId and eventId', () => {
    const raw = buildTicketQrPayload('ticket-1', 'evento-1');
    expect(JSON.parse(raw)).toEqual({ ticketId: 'ticket-1', eventId: 'evento-1' });
  });

  it('round-trips through parseTicketQrPayload', () => {
    const raw = buildTicketQrPayload('ticket-9', 'evento-9');
    expect(parseTicketQrPayload(raw)).toEqual({ ticketId: 'ticket-9', eventId: 'evento-9' });
  });

  it('rejects malformed JSON', () => {
    expect(parseTicketQrPayload('not json')).toBeNull();
  });

  it('rejects JSON missing required fields', () => {
    expect(parseTicketQrPayload(JSON.stringify({ ticketId: 'ticket-1' }))).toBeNull();
    expect(parseTicketQrPayload(JSON.stringify({ eventId: 'evento-1' }))).toBeNull();
  });

  it('rejects non-string or empty field values', () => {
    expect(parseTicketQrPayload(JSON.stringify({ ticketId: 1, eventId: 'evento-1' }))).toBeNull();
    expect(parseTicketQrPayload(JSON.stringify({ ticketId: '', eventId: 'evento-1' }))).toBeNull();
  });
});
