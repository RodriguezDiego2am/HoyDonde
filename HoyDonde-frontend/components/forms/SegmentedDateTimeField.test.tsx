import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import { SegmentedDateTimeField } from './SegmentedDateTimeField';
import { parseLocalDateTime, toUtcIso } from '@/utils/datetime';

function Harness({
  initialDate = '',
  initialTime = '',
  error,
}: {
  initialDate?: string;
  initialTime?: string;
  error?: string | null;
}) {
  const [dateValue, setDateValue] = React.useState(initialDate);
  const [timeValue, setTimeValue] = React.useState(initialTime);
  return (
    <SegmentedDateTimeField
      label="Inicio"
      dateValue={dateValue}
      timeValue={timeValue}
      onChangeDate={setDateValue}
      onChangeTime={setTimeValue}
      error={error}
      testIDPrefix="fecha-inicio"
    />
  );
}

describe('SegmentedDateTimeField', () => {
  it('precarga los segmentos a partir de dateValue/timeValue (edición)', () => {
    const { getByTestId } = render(<Harness initialDate="01/12/2026" initialTime="22:00" />);

    expect(getByTestId('fecha-inicio-day').props.value).toBe('01');
    expect(getByTestId('fecha-inicio-month').props.value).toBe('12');
    expect(getByTestId('fecha-inicio-year').props.value).toBe('2026');
    expect(getByTestId('fecha-inicio-hour').props.value).toBe('22');
    expect(getByTestId('fecha-inicio-minute').props.value).toBe('00');
  });

  it('acepta únicamente dígitos: caracteres no numéricos se descartan', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('fecha-inicio-day'), 'a1b2c');

    expect(getByTestId('fecha-inicio-day').props.value).toBe('12');
  });

  it('reparte un pegado más largo que un segmento entre los siguientes (día → mes)', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('fecha-inicio-day'), '0112');

    expect(getByTestId('fecha-inicio-day').props.value).toBe('01');
    expect(getByTestId('fecha-inicio-month').props.value).toBe('12');
  });

  it('reparte un pegado de fecha completa entre día, mes y año', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('fecha-inicio-day'), '01122026');

    expect(getByTestId('fecha-inicio-day').props.value).toBe('01');
    expect(getByTestId('fecha-inicio-month').props.value).toBe('12');
    expect(getByTestId('fecha-inicio-year').props.value).toBe('2026');
  });

  it('reparte un pegado en el grupo de hora (hora → minutos)', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('fecha-inicio-hour'), '2230');

    expect(getByTestId('fecha-inicio-hour').props.value).toBe('22');
    expect(getByTestId('fecha-inicio-minute').props.value).toBe('30');
  });

  it('retroceso en un segmento vacío borra el último dígito del segmento anterior', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('fecha-inicio-day'), '01');
    // El mes está vacío: Backspace ahí debe "volver" y borrar el último dígito del día.
    fireEvent(getByTestId('fecha-inicio-month'), 'keyPress', { nativeEvent: { key: 'Backspace' } });

    expect(getByTestId('fecha-inicio-day').props.value).toBe('0');
    expect(getByTestId('fecha-inicio-month').props.value).toBe('');
  });

  it('retroceso en un segmento con contenido no toca el segmento anterior', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent.changeText(getByTestId('fecha-inicio-day'), '01');
    fireEvent.changeText(getByTestId('fecha-inicio-month'), '12');
    fireEvent(getByTestId('fecha-inicio-month'), 'keyPress', { nativeEvent: { key: 'Backspace' } });

    expect(getByTestId('fecha-inicio-day').props.value).toBe('01');
  });

  it('el primer segmento (día) no reacciona a Backspace: no hay segmento anterior', () => {
    const { getByTestId } = render(<Harness />);

    fireEvent(getByTestId('fecha-inicio-day'), 'keyPress', { nativeEvent: { key: 'Backspace' } });

    expect(getByTestId('fecha-inicio-day').props.value).toBe('');
  });

  it('emite el valor combinado "DD/MM/AAAA"/"HH:MM" esperado, compatible con parseLocalDateTime/toUtcIso', () => {
    const onChangeDate = jest.fn();
    const onChangeTime = jest.fn();
    const { getByTestId } = render(
      <SegmentedDateTimeField
        label="Inicio"
        dateValue=""
        timeValue=""
        onChangeDate={onChangeDate}
        onChangeTime={onChangeTime}
        testIDPrefix="fecha-inicio"
      />
    );

    fireEvent.changeText(getByTestId('fecha-inicio-day'), '01122026');
    fireEvent.changeText(getByTestId('fecha-inicio-hour'), '2200');

    expect(onChangeDate).toHaveBeenLastCalledWith('01/12/2026');
    expect(onChangeTime).toHaveBeenLastCalledWith('22:00');

    const inicio = parseLocalDateTime('01/12/2026', '22:00');
    expect(inicio).not.toBeNull();
    expect(toUtcIso(inicio!)).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
  });

  it('muestra el error con rol accesible de alerta', () => {
    const { getByText } = render(<Harness error="Ingresá una fecha y hora de inicio válidas." />);

    const errorNode = getByText('Ingresá una fecha y hora de inicio válidas.');
    expect(errorNode.props.accessibilityRole).toBe('alert');
  });

  it('no muestra ningún texto de error cuando no se pasa error', () => {
    const { queryByRole } = render(<Harness />);
    expect(queryByRole('alert')).toBeNull();
  });
});
