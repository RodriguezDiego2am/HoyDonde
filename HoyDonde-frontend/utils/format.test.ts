import { formatFechaHora, formatPrecio } from './format';

describe('format', () => {
  it('formats an ISO date/time in Argentina locale without altering the underlying value', () => {
    const iso = '2026-12-01T22:00:00Z';
    const formatted = formatFechaHora(iso);
    expect(formatted).toContain('2026');
    expect(formatted).toContain('DIC');
  });

  it('returns the raw string when the date is invalid', () => {
    expect(formatFechaHora('not-a-date')).toBe('not-a-date');
  });

  it('formats a price as Argentine currency', () => {
    const formatted = formatPrecio(5000);
    expect(formatted).toContain('5.000');
  });

  it('formats zero (free ticket types are allowed)', () => {
    expect(formatPrecio(0)).toMatch(/0/);
  });
});
