import React from 'react';
import { render } from '@testing-library/react-native';

import { OcupacionAsistenciaBars } from './OcupacionAsistenciaBars';

describe('OcupacionAsistenciaBars', () => {
  it('muestra ambos porcentajes con accessibilityLabel legible', () => {
    const { getByLabelText } = render(<OcupacionAsistenciaBars porcentajeOcupacion={66.666} porcentajeAsistencia={50} />);

    expect(getByLabelText('Ocupación: 66.7%')).toBeTruthy();
    expect(getByLabelText('Asistencia: 50.0%')).toBeTruthy();
  });

  it('renderiza el texto del porcentaje visible (no depende solo del color/ancho)', () => {
    const { getAllByText } = render(<OcupacionAsistenciaBars porcentajeOcupacion={40} porcentajeAsistencia={40} />);

    expect(getAllByText('40.0%').length).toBe(2);
  });

  it('0% no rompe el render', () => {
    expect(() => render(<OcupacionAsistenciaBars porcentajeOcupacion={0} porcentajeAsistencia={0} />)).not.toThrow();
  });

  it('100% no rompe el render', () => {
    expect(() => render(<OcupacionAsistenciaBars porcentajeOcupacion={100} porcentajeAsistencia={100} />)).not.toThrow();
  });
});
