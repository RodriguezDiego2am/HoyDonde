/**
 * Entrada de fecha/hora local (DD/MM/AAAA + HH:MM) para el formulario de eventos,
 * convertida a UTC recién al armar el payload (`EventCreateRequest`/`EventUpdateRequest`
 * exigen `fechaInicio`/`fechaFin` en UTC — CLAUDE.md "Working rules"). Nunca transforma
 * un valor ya recibido de la API: eso es responsabilidad de utils/format.ts.
 */

const DATE_RE = /^(\d{2})\/(\d{2})\/(\d{4})$/;
const TIME_RE = /^(\d{2}):(\d{2})$/;

/** Interpreta "DD/MM/AAAA" + "HH:MM" como hora local del dispositivo. `null` si el formato o los valores no son válidos (incluye overflow como 31/02 que Date normalmente "corrige" silenciosamente). */
export function parseLocalDateTime(dateStr: string, timeStr: string): Date | null {
  const dateMatch = DATE_RE.exec(dateStr.trim());
  const timeMatch = TIME_RE.exec(timeStr.trim());
  if (!dateMatch || !timeMatch) return null;

  const [, ddStr, mmStr, yyyyStr] = dateMatch;
  const [, hhStr, minStr] = timeMatch;
  const dd = Number(ddStr);
  const mm = Number(mmStr);
  const yyyy = Number(yyyyStr);
  const hh = Number(hhStr);
  const min = Number(minStr);

  const date = new Date(yyyy, mm - 1, dd, hh, min, 0, 0);
  const isSameCalendarValue =
    date.getFullYear() === yyyy &&
    date.getMonth() === mm - 1 &&
    date.getDate() === dd &&
    date.getHours() === hh &&
    date.getMinutes() === min;

  return isSameCalendarValue ? date : null;
}

export function toUtcIso(date: Date): string {
  return date.toISOString();
}

/** Inverso de parseLocalDateTime + toUtcIso: para precargar el formulario de edición desde un EventResponse (ISO UTC) en los campos locales DD/MM/AAAA y HH:MM. */
export function splitIsoToLocalParts(iso: string): { date: string; time: string } {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return { date: '', time: '' };

  const dd = String(parsed.getDate()).padStart(2, '0');
  const mm = String(parsed.getMonth() + 1).padStart(2, '0');
  const yyyy = parsed.getFullYear();
  const hh = String(parsed.getHours()).padStart(2, '0');
  const min = String(parsed.getMinutes()).padStart(2, '0');

  return { date: `${dd}/${mm}/${yyyy}`, time: `${hh}:${min}` };
}
