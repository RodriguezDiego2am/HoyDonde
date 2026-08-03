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

/**
 * Variantes de solo-fecha para los filtros de rango de la Cartelera (docs/api-mvp-plan.md §7
 * Frontend 5, GET /api/events?fechaDesde&fechaHasta). Comparten el mismo formato "DD/MM/AAAA"
 * que parseLocalDateTime, pero sin hora: acá la hora no tiene sentido, "Desde"/"Hasta" describen
 * un día completo, no un instante — nunca se pide ni se fabrica un "00:00" para poder reusar
 * parseLocalDateTime.
 */

/** Interpreta "DD/MM/AAAA" como medianoche local de ese día. `null` si el formato o el valor no son válidos (incluye overflow como 31/02, igual que parseLocalDateTime). */
export function parseLocalDate(dateStr: string): Date | null {
  const match = DATE_RE.exec(dateStr.trim());
  if (!match) return null;

  const [, ddStr, mmStr, yyyyStr] = match;
  const dd = Number(ddStr);
  const mm = Number(mmStr);
  const yyyy = Number(yyyyStr);

  const date = new Date(yyyy, mm - 1, dd, 0, 0, 0, 0);
  const isSameCalendarValue = date.getFullYear() === yyyy && date.getMonth() === mm - 1 && date.getDate() === dd;

  return isSameCalendarValue ? date : null;
}

/** Medianoche local del mismo día calendario que `date` (trunca hora/minuto/segundo/ms). */
export function startOfLocalDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 0, 0, 0, 0);
}

/**
 * Medianoche local del día siguiente a `date`: límite superior EXCLUSIVO de un filtro "Hasta"
 * que debe incluir el día completo (nunca `23:59:59.999`, que puede perder el último milisegundo
 * de vigencia del día). Arma la fecha por componentes en vez de sumar 24h en milisegundos, para
 * que el propio motor de Date resuelva cambios de mes/año y horario de verano sin aritmética manual.
 */
export function nextLocalDayExclusive(date: Date): Date {
  const start = startOfLocalDay(date);
  return new Date(start.getFullYear(), start.getMonth(), start.getDate() + 1, 0, 0, 0, 0);
}

/** true si `desde` no es posterior a `hasta` comparando por día calendario local (un mismo día en ambos extremos es un rango válido de un solo día). */
export function isValidLocalDateRange(desde: Date, hasta: Date): boolean {
  return startOfLocalDay(desde).getTime() <= startOfLocalDay(hasta).getTime();
}
