import React from 'react';
import { act, fireEvent, render } from '@testing-library/react-native';

const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: { back: (...args: unknown[]) => mockBack(...args) },
}));

jest.mock('expo-haptics', () => ({
  notificationAsync: jest.fn().mockResolvedValue(undefined),
  NotificationFeedbackType: { Success: 'success', Error: 'error' },
}));

const mockValidateTicket = jest.fn();
jest.mock('@/services/ticketValidationService', () => ({
  validateTicket: (...args: unknown[]) => mockValidateTicket(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import ControlManualEntryScreen from './ControlManualEntryScreen';

describe('ControlManualEntryScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('el botón "Validar entrada" está deshabilitado hasta completar ambos campos', () => {
    const { getByText, getByPlaceholderText } = render(<ControlManualEntryScreen />);

    fireEvent.press(getByText('Validar entrada'));
    expect(mockValidateTicket).not.toHaveBeenCalled();

    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-1');
    fireEvent.press(getByText('Validar entrada'));
    expect(mockValidateTicket).not.toHaveBeenCalled();
  });

  it('recorta espacios en ambos campos antes de validar (mismo servicio y lock que el escáner)', async () => {
    mockValidateTicket.mockResolvedValue({ kind: 'valid', message: 'Ticket validado para este evento.' });
    const { getByText, getByPlaceholderText, findByText } = render(<ControlManualEntryScreen />);

    fireEvent.changeText(getByPlaceholderText('ticket-...'), '  ticket-1  ');
    fireEvent.changeText(getByPlaceholderText('evento-...'), '  evento-1  ');

    await act(async () => {
      fireEvent.press(getByText('Validar entrada'));
    });

    await findByText('Ticket validado para este evento.');
    expect(mockValidateTicket).toHaveBeenCalledTimes(1);
    expect(mockValidateTicket).toHaveBeenCalledWith({ ticketId: 'ticket-1', eventId: 'evento-1' });
  });

  it('un doble tap mientras la validación está en vuelo no duplica la llamada', async () => {
    let resolveValidate: (value: unknown) => void = () => {};
    mockValidateTicket.mockReturnValue(
      new Promise((resolve) => {
        resolveValidate = resolve;
      })
    );
    const { getByText, getByPlaceholderText } = render(<ControlManualEntryScreen />);

    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-1');
    fireEvent.changeText(getByPlaceholderText('evento-...'), 'evento-1');

    act(() => {
      fireEvent.press(getByText('Validar entrada'));
      fireEvent.press(getByText('Validar entrada'));
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveValidate({ kind: 'valid', message: 'ok' });
    });
  });

  it('muestra el resultado tipado con texto (nunca solo color)', async () => {
    mockValidateTicket.mockResolvedValue({ kind: 'anulado', message: 'El ticket fue anulado.' });
    const { getByText, getByPlaceholderText, findByText } = render(<ControlManualEntryScreen />);

    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-1');
    fireEvent.changeText(getByPlaceholderText('evento-...'), 'evento-1');

    await act(async () => {
      fireEvent.press(getByText('Validar entrada'));
    });

    await findByText('El ticket fue anulado.');
  });

  it('"Validar otra entrada" limpia el resultado, el lock y los campos', async () => {
    mockValidateTicket.mockResolvedValueOnce({ kind: 'notFound', message: 'Ticket no encontrado.' });
    const { getByText, getByPlaceholderText, findByText, queryByPlaceholderText } = render(<ControlManualEntryScreen />);

    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-1');
    fireEvent.changeText(getByPlaceholderText('evento-...'), 'evento-1');

    await act(async () => {
      fireEvent.press(getByText('Validar entrada'));
    });
    await findByText('Ticket no encontrado.');

    expect(queryByPlaceholderText('ticket-...')).toBeNull();

    fireEvent.press(getByText('Validar otra entrada'));

    expect(getByPlaceholderText('ticket-...')).toBeTruthy();

    mockValidateTicket.mockResolvedValueOnce({ kind: 'valid', message: 'Ticket validado para este evento.' });
    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-2');
    fireEvent.changeText(getByPlaceholderText('evento-...'), 'evento-2');

    await act(async () => {
      fireEvent.press(getByText('Validar entrada'));
    });

    await findByText('Ticket validado para este evento.');
    expect(mockValidateTicket).toHaveBeenCalledTimes(2);
    expect(mockValidateTicket).toHaveBeenLastCalledWith({ ticketId: 'ticket-2', eventId: 'evento-2' });
  });

  it('un error de red se muestra con texto claro', async () => {
    mockValidateTicket.mockResolvedValue({ kind: 'network', message: 'No se pudo conectar con el servidor. Verificá tu conexión.' });
    const { getByText, getByPlaceholderText, findByText } = render(<ControlManualEntryScreen />);

    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-1');
    fireEvent.changeText(getByPlaceholderText('evento-...'), 'evento-1');

    await act(async () => {
      fireEvent.press(getByText('Validar entrada'));
    });

    await findByText('No se pudo conectar con el servidor. Verificá tu conexión.');
  });

  it('un error inesperado muestra el traceId cuando está disponible', async () => {
    mockValidateTicket.mockResolvedValue({ kind: 'unexpected', message: 'Ocurrió un error inesperado.', traceId: 'trace-1' });
    const { getByText, getByPlaceholderText, findByText } = render(<ControlManualEntryScreen />);

    fireEvent.changeText(getByPlaceholderText('ticket-...'), 'ticket-1');
    fireEvent.changeText(getByPlaceholderText('evento-...'), 'evento-1');

    await act(async () => {
      fireEvent.press(getByText('Validar entrada'));
    });

    await findByText(/trace-1/);
  });
});
