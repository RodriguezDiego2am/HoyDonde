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
// eslint-disable-next-line import/first
import { buildTicketQrPayload } from '@/utils/ticketQr';

describe('TicketQRModal', () => {
  it('codifica en el QR exactamente { ticketId, eventId }, nada más', () => {
    const { getByTestId } = render(
      <TicketQRModal visible ticketId="ticket-1" eventId="evento-1" eventoNombre="Festival de Verano" onClose={() => {}} />
    );

    const encoded = getByTestId('qr-value').props.children;
    expect(encoded).toBe(buildTicketQrPayload('ticket-1', 'evento-1'));
    expect(JSON.parse(encoded)).toEqual({ ticketId: 'ticket-1', eventId: 'evento-1' });
  });

  it('muestra ambos ids como texto legible y seleccionable (fallback operativo)', () => {
    const { getByText } = render(
      <TicketQRModal visible ticketId="ticket-1" eventId="evento-1" eventoNombre="Festival de Verano" onClose={() => {}} />
    );

    expect(getByText('ticket-1').props.selectable).toBe(true);
    expect(getByText('evento-1').props.selectable).toBe(true);
  });

  it('llama a onClose al presionar Cerrar', () => {
    const onClose = jest.fn();
    const { getByText } = render(
      <TicketQRModal visible ticketId="ticket-1" eventId="evento-1" eventoNombre="Festival de Verano" onClose={onClose} />
    );

    fireEvent.press(getByText('Cerrar'));

    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
