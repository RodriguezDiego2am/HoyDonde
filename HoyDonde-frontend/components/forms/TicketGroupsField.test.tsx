import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import { TicketGroupsField, createEmptyTicketGroupDraft } from './TicketGroupsField';
import type { TicketGroupDraft } from './TicketGroupsField';

describe('TicketGroupsField', () => {
  it('renderiza una fila por cada borrador, con sus valores', () => {
    const value: TicketGroupDraft[] = [
      { key: 'a', nombre: 'General', precio: '5000', cantidadDisponible: '100' },
      { key: 'b', nombre: 'VIP', precio: '12000', cantidadDisponible: '20' },
    ];

    const { getByText, getByDisplayValue } = render(<TicketGroupsField value={value} onChange={jest.fn()} />);

    expect(getByText('Tipo de entrada 1')).toBeTruthy();
    expect(getByText('Tipo de entrada 2')).toBeTruthy();
    expect(getByDisplayValue('General')).toBeTruthy();
    expect(getByDisplayValue('VIP')).toBeTruthy();
    expect(getByDisplayValue('12000')).toBeTruthy();
  });

  it('agrega una fila vacía al presionar "Agregar tipo de entrada"', () => {
    const onChange = jest.fn();
    const { getByText } = render(<TicketGroupsField value={[]} onChange={onChange} />);

    fireEvent.press(getByText('+ Agregar tipo de entrada'));

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0][0] as TicketGroupDraft[];
    expect(next).toHaveLength(1);
    expect(next[0]).toMatchObject({ nombre: '', precio: '', cantidadDisponible: '' });
  });

  it('quita la fila correspondiente al presionar su botón de quitar', () => {
    const onChange = jest.fn();
    const value: TicketGroupDraft[] = [
      { key: 'a', nombre: 'General', precio: '5000', cantidadDisponible: '100' },
      { key: 'b', nombre: 'VIP', precio: '12000', cantidadDisponible: '20' },
    ];
    const { getByLabelText } = render(<TicketGroupsField value={value} onChange={onChange} />);

    fireEvent.press(getByLabelText('Quitar tipo de entrada 1'));

    expect(onChange).toHaveBeenCalledWith([value[1]]);
  });

  it('propaga cambios de texto solo a la fila editada', () => {
    const onChange = jest.fn();
    const value: TicketGroupDraft[] = [
      { key: 'a', nombre: 'General', precio: '5000', cantidadDisponible: '100' },
      { key: 'b', nombre: 'VIP', precio: '12000', cantidadDisponible: '20' },
    ];
    const { getAllByLabelText } = render(<TicketGroupsField value={value} onChange={onChange} />);

    fireEvent.changeText(getAllByLabelText('Precio')[0], '6000');

    expect(onChange).toHaveBeenCalledWith([
      { ...value[0], precio: '6000' },
      value[1],
    ]);
  });

  it('muestra errores por fila cuando se proveen', () => {
    const value: TicketGroupDraft[] = [{ key: 'a', nombre: '', precio: '', cantidadDisponible: '' }];
    const { getByText } = render(
      <TicketGroupsField
        value={value}
        onChange={jest.fn()}
        errors={[{ nombre: 'El nombre es obligatorio.', precio: 'El precio no puede ser negativo.' }]}
      />
    );

    expect(getByText('El nombre es obligatorio.')).toBeTruthy();
    expect(getByText('El precio no puede ser negativo.')).toBeTruthy();
  });
});

describe('createEmptyTicketGroupDraft', () => {
  it('genera claves únicas en llamadas sucesivas', () => {
    const a = createEmptyTicketGroupDraft();
    const b = createEmptyTicketGroupDraft();
    expect(a.key).not.toBe(b.key);
  });
});
