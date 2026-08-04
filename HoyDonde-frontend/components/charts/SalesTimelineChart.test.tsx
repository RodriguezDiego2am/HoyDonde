import React from 'react';
import { render } from '@testing-library/react-native';

import { SalesTimelineChart } from './SalesTimelineChart';
import type { VentasSerieBucket } from '@/services/reportService';

function bucket(overrides: Partial<VentasSerieBucket> = {}): VentasSerieBucket {
  return {
    periodoDesde: '2026-06-01T00:00:00Z',
    periodoHasta: '2026-06-02T00:00:00Z',
    etiqueta: '01/06',
    cantidadCompras: 2,
    entradasEmitidas: 3,
    importeEmitido: 100,
    ...overrides,
  };
}

describe('SalesTimelineChart', () => {
  it('sin buckets, muestra el estado vacío', () => {
    const { getByText } = render(<SalesTimelineChart buckets={[]} />);

    expect(getByText('Sin datos para el período elegido.')).toBeTruthy();
  });

  it('renderiza una columna por bucket con etiqueta y accessibilityLabel con el valor', () => {
    const buckets = [bucket({ periodoDesde: '2026-06-01T00:00:00Z', etiqueta: '01/06' }), bucket({ periodoDesde: '2026-06-02T00:00:00Z', etiqueta: '02/06', importeEmitido: 0, cantidadCompras: 0, entradasEmitidas: 0 })];

    const { getByLabelText, getByText } = render(<SalesTimelineChart buckets={buckets} />);

    expect(getByText('01/06')).toBeTruthy();
    expect(getByText('02/06')).toBeTruthy();
    expect(getByLabelText(/01\/06:.*2 compras/)).toBeTruthy();
  });

  it('bucket en cero no rompe el escalado (sin dividir por cero)', () => {
    const buckets = [bucket({ importeEmitido: 0, cantidadCompras: 0, entradasEmitidas: 0 })];

    const { getByText } = render(<SalesTimelineChart buckets={buckets} />);

    expect(getByText('—')).toBeTruthy();
  });

  it('maneja muchos buckets sin errores (scroll horizontal)', () => {
    const buckets = Array.from({ length: 40 }, (_, i) => {
      const fecha = new Date(Date.UTC(2026, 0, 1 + i));
      return bucket({ periodoDesde: fecha.toISOString(), etiqueta: `d${i}` });
    });

    const { getAllByText } = render(<SalesTimelineChart buckets={buckets} />);

    expect(getAllByText(/^d\d+$/).length).toBe(40);
  });
});
