/**
 * Formato de fechas y precios para Argentina. Solo formatea para mostrar: nunca
 * transforma el valor real enviado/recibido de la API (ISO 8601 UTC / decimal).
 */

const LOCALE = 'es-AR';

const dateFormatter = new Intl.DateTimeFormat(LOCALE, {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
});

const timeFormatter = new Intl.DateTimeFormat(LOCALE, {
  hour: '2-digit',
  minute: '2-digit',
});

const currencyFormatter = new Intl.NumberFormat(LOCALE, {
  style: 'currency',
  currency: 'ARS',
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

export function formatFecha(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return dateFormatter.format(date).replace('.', '').toUpperCase();
}

export function formatHora(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return timeFormatter.format(date);
}

export function formatFechaHora(iso: string): string {
  const fecha = formatFecha(iso);
  const hora = formatHora(iso);
  return hora ? `${fecha} · ${hora}` : fecha;
}

export function formatPrecio(value: number): string {
  return currencyFormatter.format(value);
}
