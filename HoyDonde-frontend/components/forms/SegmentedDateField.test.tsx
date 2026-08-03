import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import { SegmentedDateField } from './SegmentedDateField';

function Harness({ initialValue = '', error }: { initialValue?: string; error?: string | null }) {
  const [value, setValue] = React.useState(initialValue);
  return (
    <SegmentedDateField label="Desde" value={value} onChange={setValue} error={error} testIDPrefix="filtro-desde" />
  );
}

describe('SegmentedDateField', () => {
  it('precarga los segmentos a partir de value', () => {
    const { getByTestId } = render(<Harness initialValue="05/08/2026" />);

    expect(getByTestId('filtro-desde-day').props.value).toBe('05');
    expect(getByTestId('filtro-desde-month').props.value).toBe('08');
    expect(getByTestId('filtro-desde-year').props.value).toBe('2026');
  });

  it('no incluye ningún segmento de hora', () => {
    const { queryByTestId } = render(<Harness />);

    expect(queryByTestId('filtro-desde-hour')).toBeNull();
    expect(queryByTestId('filtro-desde-minute')).toBeNull();
  });

  it('acepta únicamente dígitos: caracteres no numéricos se descartan', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('filtro-desde-day'), 'a0b5c');

    expect(getByTestId('filtro-desde-day').props.value).toBe('05');
  });

  it('reparte un pegado de fecha completa entre día, mes y año', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('filtro-desde-day'), '05082026');

    expect(getByTestId('filtro-desde-day').props.value).toBe('05');
    expect(getByTestId('filtro-desde-month').props.value).toBe('08');
    expect(getByTestId('filtro-desde-year').props.value).toBe('2026');
  });

  it('retroceso en un segmento vacío borra el último dígito del segmento anterior', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('filtro-desde-day'), '05');
    fireEvent(getByTestId('filtro-desde-month'), 'keyPress', { nativeEvent: { key: 'Backspace' } });

    expect(getByTestId('filtro-desde-day').props.value).toBe('0');
    expect(getByTestId('filtro-desde-month').props.value).toBe('');
  });

  it('emite el valor combinado "DD/MM/AAAA", compatible con parseLocalDate', () => {
    const onChange = jest.fn();
    const { getByTestId } = render(
      <SegmentedDateField label="Desde" value="" onChange={onChange} testIDPrefix="filtro-desde" />
    );

    fireEvent.changeText(getByTestId('filtro-desde-day'), '05082026');

    expect(onChange).toHaveBeenLastCalledWith('05/08/2026');
  });

  it('muestra el error con rol accesible de alerta', () => {
    const { getByText } = render(<Harness error="Ingresá una fecha válida." />);

    const errorNode = getByText('Ingresá una fecha válida.');
    expect(errorNode.props.accessibilityRole).toBe('alert');
  });

  it('no muestra ningún texto de error cuando no se pasa error', () => {
    const { queryByRole } = render(<Harness />);
    expect(queryByRole('alert')).toBeNull();
  });
});
