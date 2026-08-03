import React from 'react';
import { act, fireEvent, render } from '@testing-library/react-native';

const mockBack = jest.fn();
const mockPush = jest.fn();
const mockReplace = jest.fn();
jest.mock('expo-router', () => ({
  router: {
    back: (...args: unknown[]) => mockBack(...args),
    push: (...args: unknown[]) => mockPush(...args),
    replace: (...args: unknown[]) => mockReplace(...args),
  },
}));

let mockIsFocused = true;
jest.mock('@react-navigation/native', () => ({
  useIsFocused: () => mockIsFocused,
}));

type MockPermission = { granted: boolean; status: 'undetermined' | 'granted' | 'denied'; canAskAgain: boolean } | null;
let mockPermission: MockPermission = null;
const mockRequestPermission = jest.fn().mockResolvedValue(undefined);
jest.mock('expo-camera', () => {
  const ReactActual = require('react');
  const { View } = require('react-native');
  return {
    CameraView: (props: Record<string, unknown>) => ReactActual.createElement(View, props),
    useCameraPermissions: () => [mockPermission, mockRequestPermission],
  };
});

const mockOpenSettings = jest.fn();
jest.mock('expo-linking', () => ({ openSettings: (...args: unknown[]) => mockOpenSettings(...args) }));

jest.mock('expo-haptics', () => ({
  notificationAsync: jest.fn().mockResolvedValue(undefined),
  NotificationFeedbackType: { Success: 'success', Error: 'error' },
}));

const mockValidateTicket = jest.fn();
jest.mock('@/services/ticketValidationService', () => ({
  validateTicket: (...args: unknown[]) => mockValidateTicket(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import ControlScanScreen from './ControlScanScreen';

function scanData(getByTestId: (id: string) => any, data: string) {
  fireEvent(getByTestId('control-camera-view'), 'barcodeScanned', { data });
}

function scanPayload(getByTestId: (id: string) => any, payload: { ticketId: string; eventId: string }) {
  scanData(getByTestId, JSON.stringify(payload));
}

describe('ControlScanScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockIsFocused = true;
    mockPermission = null;
  });

  it('muestra un estado de carga mientras se comprueba el permiso', () => {
    const { getByText } = render(<ControlScanScreen />);
    expect(getByText('Comprobando el permiso de cámara')).toBeTruthy();
  });

  it('permiso no solicitado (undetermined): ofrece pedirlo', () => {
    mockPermission = { granted: false, status: 'undetermined', canAskAgain: true };
    const { getByText } = render(<ControlScanScreen />);

    fireEvent.press(getByText('Solicitar permiso'));
    expect(mockRequestPermission).toHaveBeenCalledTimes(1);
  });

  it('permiso denegado pero se puede volver a pedir: ofrece pedirlo de nuevo', () => {
    mockPermission = { granted: false, status: 'denied', canAskAgain: true };
    const { getByText } = render(<ControlScanScreen />);

    expect(getByText('El permiso de cámara fue denegado.')).toBeTruthy();
    fireEvent.press(getByText('Solicitar permiso'));
    expect(mockRequestPermission).toHaveBeenCalledTimes(1);
  });

  it('permiso denegado permanentemente: ofrece abrir la configuración del sistema', () => {
    mockPermission = { granted: false, status: 'denied', canAskAgain: false };
    const { getByText, queryByText } = render(<ControlScanScreen />);

    expect(queryByText('Solicitar permiso')).toBeNull();
    fireEvent.press(getByText('Abrir configuración'));
    expect(mockOpenSettings).toHaveBeenCalledTimes(1);
  });

  it('permiso concedido y pantalla enfocada: muestra la cámara', () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    const { getByTestId } = render(<ControlScanScreen />);
    expect(getByTestId('control-camera-view')).toBeTruthy();
  });

  it('pantalla sin foco: la cámara no se monta', () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    mockIsFocused = false;
    const { queryByTestId, getByText } = render(<ControlScanScreen />);
    expect(queryByTestId('control-camera-view')).toBeNull();
    expect(getByText('Cámara en pausa')).toBeTruthy();
  });

  it('un QR válido llama a validateTicket una sola vez con los ids exactos', async () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    mockValidateTicket.mockResolvedValue({ kind: 'valid', message: 'Ticket validado para este evento.' });
    const { getByTestId, findByText } = render(<ControlScanScreen />);

    act(() => {
      scanPayload(getByTestId, { ticketId: 'ticket-1', eventId: 'evento-1' });
    });

    await findByText('Ticket validado para este evento.');
    expect(mockValidateTicket).toHaveBeenCalledTimes(1);
    expect(mockValidateTicket).toHaveBeenCalledWith({ ticketId: 'ticket-1', eventId: 'evento-1' });
  });

  it('varios frames del mismo QR no duplican la validación', async () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    let resolveValidate: (value: unknown) => void = () => {};
    mockValidateTicket.mockReturnValue(
      new Promise((resolve) => {
        resolveValidate = resolve;
      })
    );
    const { getByTestId } = render(<ControlScanScreen />);

    act(() => {
      scanPayload(getByTestId, { ticketId: 'ticket-1', eventId: 'evento-1' });
      scanPayload(getByTestId, { ticketId: 'ticket-1', eventId: 'evento-1' });
      scanPayload(getByTestId, { ticketId: 'ticket-1', eventId: 'evento-1' });
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveValidate({ kind: 'valid', message: 'ok' });
    });
  });

  it('un QR inválido (JSON sin la forma esperada) se rechaza localmente y no llama a la API', () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    const { getByTestId, getByText } = render(<ControlScanScreen />);

    act(() => {
      scanData(getByTestId, 'esto no es un JSON de ticket');
    });

    expect(mockValidateTicket).not.toHaveBeenCalled();
    expect(getByText('Ese código no corresponde a una entrada de HoyDonde?.')).toBeTruthy();
  });

  it('un QR con una URL arbitraria tampoco llama a la API', () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    const { getByTestId } = render(<ControlScanScreen />);

    act(() => {
      scanData(getByTestId, 'https://example.com/not-a-ticket');
    });

    expect(mockValidateTicket).not.toHaveBeenCalled();
  });

  it('"Escanear siguiente" limpia el resultado y el lock, y remonta la cámara', async () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    mockValidateTicket.mockResolvedValue({ kind: 'notFound', message: 'Ticket no encontrado.' });
    const { getByTestId, findByText, getByText, queryByTestId } = render(<ControlScanScreen />);

    act(() => {
      scanPayload(getByTestId, { ticketId: 'ticket-1', eventId: 'evento-1' });
    });
    await findByText('Ticket no encontrado.');
    expect(queryByTestId('control-camera-view')).toBeNull();

    fireEvent.press(getByText('Escanear siguiente'));

    expect(getByTestId('control-camera-view')).toBeTruthy();

    mockValidateTicket.mockResolvedValue({ kind: 'valid', message: 'Ticket validado para este evento.' });
    act(() => {
      scanPayload(getByTestId, { ticketId: 'ticket-2', eventId: 'evento-2' });
    });
    await findByText('Ticket validado para este evento.');
    expect(mockValidateTicket).toHaveBeenCalledTimes(2);
    expect(mockValidateTicket).toHaveBeenLastCalledWith({ ticketId: 'ticket-2', eventId: 'evento-2' });
  });

  it('la cámara queda inactiva (desmontada) mientras se muestra un resultado', async () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    mockValidateTicket.mockResolvedValue({ kind: 'notAuthorized', message: 'No autorizado para validar tickets de este evento.' });
    const { getByTestId, findByText, queryByTestId } = render(<ControlScanScreen />);

    act(() => {
      scanPayload(getByTestId, { ticketId: 'ticket-1', eventId: 'evento-1' });
    });

    await findByText('No autorizado para validar tickets de este evento.');
    expect(queryByTestId('control-camera-view')).toBeNull();
  });

  it('un error de montaje de cámara se muestra de forma recuperable', () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    const { getByTestId, getByText, queryByTestId } = render(<ControlScanScreen />);

    act(() => {
      fireEvent(getByTestId('control-camera-view'), 'mountError', { message: 'No se pudo abrir el dispositivo de cámara.' });
    });

    expect(getByText('No se pudo iniciar la cámara.')).toBeTruthy();
    expect(queryByTestId('control-camera-view')).toBeNull();

    fireEvent.press(getByText('Reintentar'));
    expect(getByTestId('control-camera-view')).toBeTruthy();
  });

  it('ofrece la alternativa de ingreso manual', () => {
    mockPermission = { granted: true, status: 'granted', canAskAgain: true };
    const { getByText } = render(<ControlScanScreen />);

    fireEvent.press(getByText('Ingresar código manualmente'));
    expect(mockReplace).toHaveBeenCalledWith('/control/manual');
  });
});
