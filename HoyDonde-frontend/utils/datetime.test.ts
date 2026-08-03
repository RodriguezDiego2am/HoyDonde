import {
  isValidLocalDateRange,
  nextLocalDayExclusive,
  parseLocalDate,
  parseLocalDateTime,
  splitIsoToLocalParts,
  startOfLocalDay,
  toUtcIso,
} from './datetime';

describe('parseLocalDateTime', () => {
  it('parsea una fecha y hora local válidas', () => {
    const date = parseLocalDateTime('01/12/2026', '22:00');
    expect(date).not.toBeNull();
    expect(date?.getFullYear()).toBe(2026);
    expect(date?.getMonth()).toBe(11);
    expect(date?.getDate()).toBe(1);
    expect(date?.getHours()).toBe(22);
    expect(date?.getMinutes()).toBe(0);
  });

  it('rechaza un formato de fecha inválido', () => {
    expect(parseLocalDateTime('2026-12-01', '22:00')).toBeNull();
    expect(parseLocalDateTime('', '22:00')).toBeNull();
  });

  it('rechaza un formato de hora inválido', () => {
    expect(parseLocalDateTime('01/12/2026', '22h00')).toBeNull();
    expect(parseLocalDateTime('01/12/2026', '')).toBeNull();
  });

  it('rechaza valores de calendario imposibles (31/02, 25:99) en lugar de dejar que Date los "corrija"', () => {
    expect(parseLocalDateTime('31/02/2026', '10:00')).toBeNull();
    expect(parseLocalDateTime('01/13/2026', '10:00')).toBeNull();
    expect(parseLocalDateTime('01/12/2026', '25:99')).toBeNull();
  });
});

describe('toUtcIso + splitIsoToLocalParts', () => {
  it('son inversas entre sí para una fecha/hora local dada', () => {
    const original = parseLocalDateTime('01/12/2026', '22:00')!;
    const iso = toUtcIso(original);

    const { date, time } = splitIsoToLocalParts(iso);
    expect(date).toBe('01/12/2026');
    expect(time).toBe('22:00');
  });

  it('produce un string ISO UTC (terminado en Z) apto para el contrato de la API', () => {
    const date = parseLocalDateTime('01/12/2026', '22:00')!;
    expect(toUtcIso(date)).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
  });

  it('splitIsoToLocalParts devuelve vacío para un ISO inválido, en vez de lanzar', () => {
    expect(splitIsoToLocalParts('no-es-una-fecha')).toEqual({ date: '', time: '' });
  });
});

describe('parseLocalDate', () => {
  it('parsea una fecha local válida como medianoche de ese día', () => {
    const date = parseLocalDate('05/08/2026');
    expect(date).not.toBeNull();
    expect(date?.getFullYear()).toBe(2026);
    expect(date?.getMonth()).toBe(7);
    expect(date?.getDate()).toBe(5);
    expect(date?.getHours()).toBe(0);
    expect(date?.getMinutes()).toBe(0);
    expect(date?.getSeconds()).toBe(0);
    expect(date?.getMilliseconds()).toBe(0);
  });

  it('rechaza un formato inválido', () => {
    expect(parseLocalDate('2026-08-05')).toBeNull();
    expect(parseLocalDate('')).toBeNull();
  });

  it('rechaza valores de calendario imposibles (31/02) en lugar de dejar que Date los "corrija"', () => {
    expect(parseLocalDate('31/02/2026')).toBeNull();
    expect(parseLocalDate('01/13/2026')).toBeNull();
  });
});

describe('startOfLocalDay', () => {
  it('trunca hora/minuto/segundo/ms manteniendo el mismo día', () => {
    const withTime = new Date(2026, 7, 5, 22, 30, 15, 500);
    const start = startOfLocalDay(withTime);

    expect(start.getFullYear()).toBe(2026);
    expect(start.getMonth()).toBe(7);
    expect(start.getDate()).toBe(5);
    expect(start.getHours()).toBe(0);
    expect(start.getMinutes()).toBe(0);
    expect(start.getSeconds()).toBe(0);
    expect(start.getMilliseconds()).toBe(0);
  });
});

describe('nextLocalDayExclusive', () => {
  it('devuelve la medianoche del día siguiente, nunca 23:59:59.999', () => {
    const date = parseLocalDate('05/08/2026')!;
    const next = nextLocalDayExclusive(date);

    expect(next.getDate()).toBe(6);
    expect(next.getMonth()).toBe(7);
    expect(next.getHours()).toBe(0);
    expect(next.getMinutes()).toBe(0);
    expect(next.getSeconds()).toBe(0);
    expect(next.getMilliseconds()).toBe(0);
  });

  it('resuelve correctamente el cambio de mes', () => {
    const date = parseLocalDate('31/08/2026')!;
    const next = nextLocalDayExclusive(date);

    expect(next.getFullYear()).toBe(2026);
    expect(next.getMonth()).toBe(8); // septiembre
    expect(next.getDate()).toBe(1);
  });

  it('resuelve correctamente el cambio de año', () => {
    const date = parseLocalDate('31/12/2026')!;
    const next = nextLocalDayExclusive(date);

    expect(next.getFullYear()).toBe(2027);
    expect(next.getMonth()).toBe(0); // enero
    expect(next.getDate()).toBe(1);
  });

  it('resuelve correctamente un año bisiesto (29/02)', () => {
    const date = parseLocalDate('28/02/2028')!;
    const next = nextLocalDayExclusive(date);

    expect(next.getFullYear()).toBe(2028);
    expect(next.getMonth()).toBe(1); // febrero
    expect(next.getDate()).toBe(29);
  });
});

describe('isValidLocalDateRange', () => {
  it('acepta Desde anterior a Hasta', () => {
    const desde = parseLocalDate('05/08/2026')!;
    const hasta = parseLocalDate('10/08/2026')!;
    expect(isValidLocalDateRange(desde, hasta)).toBe(true);
  });

  it('acepta Desde igual a Hasta (rango de un solo día)', () => {
    const desde = parseLocalDate('05/08/2026')!;
    const hasta = parseLocalDate('05/08/2026')!;
    expect(isValidLocalDateRange(desde, hasta)).toBe(true);
  });

  it('rechaza Desde posterior a Hasta', () => {
    const desde = parseLocalDate('10/08/2026')!;
    const hasta = parseLocalDate('05/08/2026')!;
    expect(isValidLocalDateRange(desde, hasta)).toBe(false);
  });
});

describe('rango Desde/Hasta -> ISO UTC (contrato real de GET /api/events)', () => {
  it('Desde: inicio de ese día local, convertido a UTC', () => {
    const desde = parseLocalDate('05/08/2026')!;
    const iso = toUtcIso(startOfLocalDay(desde));
    expect(iso).toMatch(/^2026-08-\d{2}T\d{2}:00:00\.000Z$/);
  });

  it('Hasta: inicio del día SIGUIENTE local (exclusivo), convertido a UTC', () => {
    const hasta = parseLocalDate('10/08/2026')!;
    const iso = toUtcIso(nextLocalDayExclusive(hasta));
    expect(iso).toMatch(/^2026-08-\d{2}T\d{2}:00:00\.000Z$/);

    const parsedBack = new Date(iso);
    expect(parsedBack.getUTCDate() === 11 || parsedBack.getUTCDate() === 10).toBe(true);
  });
});
