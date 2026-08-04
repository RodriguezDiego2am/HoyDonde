import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import { TopEventosBarChart } from './TopEventosBarChart';

describe('TopEventosBarChart', () => {
  it('sin items, muestra el estado vacío', () => {
    const { getByText } = render(<TopEventosBarChart items={[]} />);

    expect(getByText('Sin eventos con ventas en el período elegido.')).toBeTruthy();
  });

  it('renderiza nombre, importe y accessibilityLabel con el valor por cada evento', () => {
    const items = [
      { key: 'event-1', nombre: 'Festival', importeEmitido: 500, entradasEmitidas: 10 },
      { key: 'event-2', nombre: 'Maratón', importeEmitido: 100, entradasEmitidas: 2 },
    ];

    const { getByText, getByLabelText } = render(<TopEventosBarChart items={items} />);

    expect(getByText('Festival')).toBeTruthy();
    expect(getByText('Maratón')).toBeTruthy();
    expect(getByLabelText(/Festival:.*10 entradas/)).toBeTruthy();
  });

  it('todos los importes en cero: no rompe el cálculo de la barra', () => {
    const items = [
      { key: 'event-1', nombre: 'Festival', importeEmitido: 0 },
      { key: 'event-2', nombre: 'Maratón', importeEmitido: 0 },
    ];

    expect(() => render(<TopEventosBarChart items={items} />)).not.toThrow();
  });

  it('empate de importes: ambos se renderizan sin error', () => {
    const items = [
      { key: 'event-1', nombre: 'Festival', importeEmitido: 100 },
      { key: 'event-2', nombre: 'Maratón', importeEmitido: 100 },
    ];

    const { getByText } = render(<TopEventosBarChart items={items} />);

    expect(getByText('Festival')).toBeTruthy();
    expect(getByText('Maratón')).toBeTruthy();
  });

  it('con onPressItem, cada fila es presionable y dispara el callback con el item', () => {
    const items = [{ key: 'event-1', nombre: 'Festival', importeEmitido: 500, entradasEmitidas: 10 }];
    const onPressItem = jest.fn();

    const { getByLabelText } = render(<TopEventosBarChart items={items} onPressItem={onPressItem} />);

    fireEvent.press(getByLabelText(/Ver solo este evento/));

    expect(onPressItem).toHaveBeenCalledWith(items[0]);
  });

  it('sin onPressItem, las filas no son botones presionables', () => {
    const items = [{ key: 'event-1', nombre: 'Festival', importeEmitido: 500 }];

    const { queryByLabelText } = render(<TopEventosBarChart items={items} />);

    expect(queryByLabelText(/Ver solo este evento/)).toBeNull();
  });
});
