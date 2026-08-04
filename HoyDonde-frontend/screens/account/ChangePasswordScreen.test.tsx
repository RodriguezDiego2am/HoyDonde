import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockReplace = jest.fn();
const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: {
    replace: (...args: unknown[]) => mockReplace(...args),
    back: (...args: unknown[]) => mockBack(...args),
  },
}));

let mockCurrentUser: { email: string | null } | null = { email: 'organizador@hoydonde.com' };
jest.mock('@/config/firebase', () => ({
  auth: {
    get currentUser() {
      return mockCurrentUser;
    },
  },
}));

const mockCredential = jest.fn((email: string, password: string) => ({ email, password, providerId: 'password' }));
const mockReauthenticateWithCredential = jest.fn();
const mockUpdatePassword = jest.fn();
jest.mock('firebase/auth', () => ({
  EmailAuthProvider: { credential: (...args: unknown[]) => (mockCredential as any)(...args) },
  reauthenticateWithCredential: (...args: unknown[]) => (mockReauthenticateWithCredential as any)(...args),
  updatePassword: (...args: unknown[]) => (mockUpdatePassword as any)(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import ChangePasswordScreen from './ChangePasswordScreen';

describe('ChangePasswordScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockCurrentUser = { email: 'organizador@hoydonde.com' };
    mockReauthenticateWithCredential.mockResolvedValue(undefined);
    mockUpdatePassword.mockResolvedValue(undefined);
  });

  it('sin sesión activa, pide volver a iniciar sesión en vez de mostrar el formulario', () => {
    mockCurrentUser = null;
    const { getByText, queryByPlaceholderText } = render(<ChangePasswordScreen />);

    expect(getByText('Tu sesión expiró. Volvé a iniciar sesión para cambiar tu contraseña.')).toBeTruthy();
    expect(queryByPlaceholderText('Tu contraseña actual')).toBeNull();

    fireEvent.press(getByText('Ir a iniciar sesión'));
    expect(mockReplace).toHaveBeenCalledWith('/login');
  });

  it('valida campos obligatorios sin llamar a Firebase', () => {
    const { getByText } = render(<ChangePasswordScreen />);

    fireEvent.press(getByText('Cambiar contraseña'));

    expect(getByText('Ingresá tu contraseña actual.')).toBeTruthy();
    expect(getByText('Ingresá una contraseña nueva.')).toBeTruthy();
    expect(getByText('Confirmá la contraseña nueva.')).toBeTruthy();
    expect(mockReauthenticateWithCredential).not.toHaveBeenCalled();
  });

  it('rechaza una confirmación que no coincide', () => {
    const { getByPlaceholderText, getByText } = render(<ChangePasswordScreen />);

    fireEvent.changeText(getByPlaceholderText('Tu contraseña actual'), 'actual123');
    fireEvent.changeText(getByPlaceholderText('Al menos 6 caracteres'), 'nuevaPass1');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña nueva'), 'otraCosa');
    fireEvent.press(getByText('Cambiar contraseña'));

    expect(getByText('Las contraseñas no coinciden.')).toBeTruthy();
    expect(mockReauthenticateWithCredential).not.toHaveBeenCalled();
  });

  it('rechaza una contraseña nueva igual a la actual', () => {
    const { getByPlaceholderText, getByText } = render(<ChangePasswordScreen />);

    fireEvent.changeText(getByPlaceholderText('Tu contraseña actual'), 'igualita1');
    fireEvent.changeText(getByPlaceholderText('Al menos 6 caracteres'), 'igualita1');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña nueva'), 'igualita1');
    fireEvent.press(getByText('Cambiar contraseña'));

    expect(getByText('La contraseña nueva no puede ser igual a la actual.')).toBeTruthy();
    expect(mockReauthenticateWithCredential).not.toHaveBeenCalled();
  });

  it('reautentica antes de updatePassword y muestra éxito', async () => {
    const { getByPlaceholderText, getByText } = render(<ChangePasswordScreen />);

    fireEvent.changeText(getByPlaceholderText('Tu contraseña actual'), 'actual123');
    fireEvent.changeText(getByPlaceholderText('Al menos 6 caracteres'), 'nuevaPass1');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña nueva'), 'nuevaPass1');
    fireEvent.press(getByText('Cambiar contraseña'));

    await waitFor(() => expect(getByText('Tu contraseña se cambió correctamente.')).toBeTruthy());

    expect(mockCredential).toHaveBeenCalledWith('organizador@hoydonde.com', 'actual123');
    expect(mockReauthenticateWithCredential).toHaveBeenCalledTimes(1);
    expect(mockUpdatePassword).toHaveBeenCalledTimes(1);
    expect(mockUpdatePassword.mock.calls[0][1]).toBe('nuevaPass1');

    const reauthOrder = mockReauthenticateWithCredential.mock.invocationCallOrder[0];
    const updateOrder = mockUpdatePassword.mock.invocationCallOrder[0];
    expect(reauthOrder).toBeLessThan(updateOrder);

    fireEvent.press(getByText('Listo'));
    expect(mockBack).toHaveBeenCalledTimes(1);
  });

  it('muestra un mensaje claro cuando la contraseña actual es incorrecta, y nunca llama a updatePassword', async () => {
    mockReauthenticateWithCredential.mockRejectedValueOnce({ code: 'auth/wrong-password' });
    const { getByPlaceholderText, getByText } = render(<ChangePasswordScreen />);

    fireEvent.changeText(getByPlaceholderText('Tu contraseña actual'), 'incorrecta');
    fireEvent.changeText(getByPlaceholderText('Al menos 6 caracteres'), 'nuevaPass1');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña nueva'), 'nuevaPass1');
    fireEvent.press(getByText('Cambiar contraseña'));

    await waitFor(() => expect(getByText('La contraseña actual no es correcta.')).toBeTruthy());
    expect(mockUpdatePassword).not.toHaveBeenCalled();
  });

  it('pide volver a iniciar sesión cuando Firebase exige un login reciente', async () => {
    mockReauthenticateWithCredential.mockRejectedValueOnce({ code: 'auth/requires-recent-login' });
    const { getByPlaceholderText, getByText } = render(<ChangePasswordScreen />);

    fireEvent.changeText(getByPlaceholderText('Tu contraseña actual'), 'actual123');
    fireEvent.changeText(getByPlaceholderText('Al menos 6 caracteres'), 'nuevaPass1');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña nueva'), 'nuevaPass1');
    fireEvent.press(getByText('Cambiar contraseña'));

    await waitFor(() =>
      expect(getByText('Tu sesión expiró por seguridad. Volvé a iniciar sesión e intentá de nuevo.')).toBeTruthy()
    );
  });

  it('bloquea el doble envío mientras hay una solicitud en curso', async () => {
    let resolveReauth!: () => void;
    mockReauthenticateWithCredential.mockReturnValueOnce(
      new Promise<void>((resolve) => {
        resolveReauth = resolve;
      })
    );
    const { getByPlaceholderText, getByText, queryByText } = render(<ChangePasswordScreen />);

    fireEvent.changeText(getByPlaceholderText('Tu contraseña actual'), 'actual123');
    fireEvent.changeText(getByPlaceholderText('Al menos 6 caracteres'), 'nuevaPass1');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña nueva'), 'nuevaPass1');

    fireEvent.press(getByText('Cambiar contraseña'));

    // El botón pasa a estado de carga (sin texto presionable): un segundo toque no puede
    // disparar una segunda solicitud mientras la primera sigue en vuelo.
    expect(queryByText('Cambiar contraseña')).toBeNull();
    expect(mockReauthenticateWithCredential).toHaveBeenCalledTimes(1);

    resolveReauth();
    await waitFor(() => expect(getByText('Tu contraseña se cambió correctamente.')).toBeTruthy());
    expect(mockReauthenticateWithCredential).toHaveBeenCalledTimes(1);
  });
});
