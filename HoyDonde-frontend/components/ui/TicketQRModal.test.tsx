import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

jest.mock('react-native-qrcode-svg', () => {
  const ReactActual = require('react');
  const { Text } = require('react-native');
  return function MockQRCode({ value }: { value: string }) {
    return ReactActual.createElement(Text, { testID: 'qr-value' }, value);
  };
});

// eslint-disable-next-line import/first -- debe importarse después del jest.mock de react-native-qrcode-svg
import { TicketQRModal } from './TicketQRModal';

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUv';

describe('TicketQRModal', () => {
  it('codifica en el QR exactamente el token del backend, sin ids ni JSON', () => {
    const { getByTestId } = render(
      <TicketQRModal visible qrToken={TOKEN} eventoNombre="Festival de Verano" onClose={() => {}} />
    );

    expect(getByTestId('qr-value').props.children).toBe(TOKEN);
  });

  it('muestra el código como texto legible y seleccionable (fallback operativo)', () => {
    const { getAllByText } = render(
      <TicketQRModal visible qrToken={TOKEN} eventoNombre="Festival de Verano" onClose={() => {}} />
    );

    // The QR mock also renders the value as text; the readable fallback is the selectable one.
    expect(getAllByText(TOKEN).some((node) => node.props.selectable === true)).toBe(true);
  });

  it('ya no afirma que el QR "no tiene firma digital" y avisa que se invalida al transferir', () => {
    const { queryByText, getByText } = render(
      <TicketQRModal visible qrToken={TOKEN} eventoNombre="Festival de Verano" onClose={() => {}} />
    );

    expect(queryByText(/firma digital/i)).toBeNull();
    expect(getByText(/un QR anterior deja de valer/i)).toBeTruthy();
  });

  it('sin token disponible no dibuja ningún QR y lo explica', () => {
    const { queryByTestId, getByText } = render(
      <TicketQRModal visible qrToken={null} eventoNombre="Festival de Verano" onClose={() => {}} />
    );

    expect(queryByTestId('qr-value')).toBeNull();
    expect(getByText(/no tiene un QR disponible/i)).toBeTruthy();
  });

  it('llama a onClose al presionar Cerrar', () => {
    const onClose = jest.fn();
    const { getByText } = render(
      <TicketQRModal visible qrToken={TOKEN} eventoNombre="Festival de Verano" onClose={onClose} />
    );

    fireEvent.press(getByText('Cerrar'));

    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
