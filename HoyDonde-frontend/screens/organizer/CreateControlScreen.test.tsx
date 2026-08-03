import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockBack = jest.fn();
let mockParams: { id?: string } = { id: 'evento-1' };
jest.mock('expo-router', () => ({
  router: { back: (...args: unknown[]) => mockBack(...args) },
  useLocalSearchParams: () => mockParams,
}));

const mockRegisterControl = jest.fn();
jest.mock('@/services/userProvisioningService', () => ({
  userProvisioningService: { registerControl: (...args: unknown[]) => mockRegisterControl(...args) },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import CreateControlScreen from './CreateControlScreen';

describe('CreateControlScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockParams = { id: 'evento-1' };
  });

  it('pide únicamente userName y contraseña (los campos reales de RegisterControlDto)', () => {
    const { getByPlaceholderText, getByText, queryByPlaceholderText } = render(<CreateControlScreen />);

    expect(getByPlaceholderText('control_puerta_norte')).toBeTruthy();
    expect(getByPlaceholderText('Contraseña temporal')).toBeTruthy();
    expect(getByText('Crear Control')).toBeTruthy();
    expect(queryByPlaceholderText(/email/i)).toBeNull();
  });

  it('envía exactamente {userName, password, eventId} usando el id de la ruta', async () => {
    mockRegisterControl.mockResolvedValueOnce({ message: 'Control creado exitosamente.', usuarioId: 'u', personaId: 'p' });
    const { getByPlaceholderText, getByText, findByText, queryByText } = render(<CreateControlScreen />);

    fireEvent.changeText(getByPlaceholderText('control_puerta_norte'), 'control_puerta_norte');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Control'));

    await waitFor(() =>
      expect(mockRegisterControl).toHaveBeenCalledWith({
        userName: 'control_puerta_norte',
        password: 'segura123',
        eventId: 'evento-1',
      })
    );

    expect(await findByText('Control creado exitosamente.')).toBeTruthy();
    expect(await findByText('Iniciá sesión con el usuario:')).toBeTruthy();
    expect(await findByText('control_puerta_norte')).toBeTruthy();
    expect(queryByText('u')).toBeNull();
    expect(queryByText('p')).toBeNull();
  });

  it('evita el doble envío mientras la solicitud está en curso', async () => {
    let resolveCall!: (value: unknown) => void;
    mockRegisterControl.mockReturnValue(
      new Promise((resolve) => {
        resolveCall = resolve;
      })
    );
    const { getByPlaceholderText, getByText } = render(<CreateControlScreen />);

    fireEvent.changeText(getByPlaceholderText('control_puerta_norte'), 'control_puerta_norte');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');

    const submitButton = getByText('Crear Control');
    fireEvent.press(submitButton);
    fireEvent.press(submitButton);

    expect(mockRegisterControl).toHaveBeenCalledTimes(1);
    resolveCall({ message: 'ok', usuarioId: 'u', personaId: 'p' });
  });

  it('mapea IDENTITY_EMAIL_ALREADY_EXISTS a un mensaje sobre el nombre de usuario', async () => {
    mockRegisterControl.mockRejectedValueOnce(
      new ApiError({ code: 'IDENTITY_EMAIL_ALREADY_EXISTS', message: 'ya existe', traceId: 't' }, 409)
    );
    const { getByPlaceholderText, getByText, findByText } = render(<CreateControlScreen />);

    fireEvent.changeText(getByPlaceholderText('control_puerta_norte'), 'repetido');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Control'));

    expect(await findByText('Ya existe una cuenta con ese nombre de usuario.')).toBeTruthy();
  });

  it('vuelve al evento al presionar "Volver al evento" desde la confirmación', async () => {
    mockRegisterControl.mockResolvedValueOnce({ message: 'ok', usuarioId: 'u', personaId: 'p' });
    const { getByPlaceholderText, getByText, findByText } = render(<CreateControlScreen />);

    fireEvent.changeText(getByPlaceholderText('control_puerta_norte'), 'control_puerta_norte');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Control'));

    fireEvent.press(await findByText('Volver al evento'));
    expect(mockBack).toHaveBeenCalledTimes(1);
  });
});
