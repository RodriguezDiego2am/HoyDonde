import { parseLocalDateTime, splitIsoToLocalParts, toUtcIso } from './datetime';

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
