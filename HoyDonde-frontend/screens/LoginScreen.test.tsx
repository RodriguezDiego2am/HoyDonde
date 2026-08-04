import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';
import LoginScreen from './LoginScreen';

const mockPush = jest.fn();
const mockReplace = jest.fn();
let mockReturnTo: string | undefined;

jest.mock('expo-router', () => ({
  router: {
    push: (...args: unknown[]) => mockPush(...args),
    replace: (...args: unknown[]) => mockReplace(...args),
  },
  useLocalSearchParams: () => ({ returnTo: mockReturnTo }),
}));

const mockLoginWithEmail = jest.fn();
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ loginWithEmail: mockLoginWithEmail }),
}));

// LoginScreen renderiza ForgotPasswordModal (aunque cerrado), que importa firebase/auth y
// @/config/firebase — mismo mock que el resto de las pantallas que tocan Firebase Auth. El
// comportamiento propio del modal (email válido/inválido, mensaje genérico, doble envío, aviso
// de Control) se cubre en components/auth/ForgotPasswordModal.test.tsx; acá solo se verifica que
// el link de Login lo abre/cierra.
const mockSendPasswordResetEmail = jest.fn();
jest.mock('firebase/auth', () => ({
  sendPasswordResetEmail: (...args: unknown[]) => (mockSendPasswordResetEmail as any)(...args),
}));
jest.mock('@/config/firebase', () => ({ auth: {} }));

describe('LoginScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockReturnTo = undefined;
  });

  it('renders the field as "Email o usuario", not just "Email"', () => {
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    expect(getByText('Email o usuario')).toBeTruthy();
    expect(getByPlaceholderText('ejemplo@correo.com o tu usuario')).toBeTruthy();
  });

  it('shows validation errors on empty submit and does not call loginWithEmail', () => {
    const { getByText } = render(<LoginScreen />);

    fireEvent.press(getByText('Ingresar'));

    expect(getByText('El email o usuario es obligatorio')).toBeTruthy();
    expect(getByText('La contraseña es obligatoria')).toBeTruthy();
    expect(mockLoginWithEmail).not.toHaveBeenCalled();
  });

  it('logs in with a normal email unchanged', async () => {
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(mockLoginWithEmail).toHaveBeenCalledWith('cliente@hoydonde.com', 'secret123'));
    expect(mockReplace).toHaveBeenCalledWith('/(tabs)');
  });

  it('logs in a Control account by resolving its username to the synthetic email, without ever displaying it', async () => {
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText, queryByText, queryByDisplayValue } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), 'control_puerta_norte');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() =>
      expect(mockLoginWithEmail).toHaveBeenCalledWith('control_puerta_norte@control.hoydonde.com', 'secret123')
    );
    expect(queryByText('control_puerta_norte@control.hoydonde.com')).toBeNull();
    expect(queryByDisplayValue('control_puerta_norte@control.hoydonde.com')).toBeNull();
  });

  it('trims surrounding whitespace before resolving, for both email and Control username', async () => {
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), '  control_puerta_norte  ');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() =>
      expect(mockLoginWithEmail).toHaveBeenCalledWith('control_puerta_norte@control.hoydonde.com', 'secret123')
    );
  });

  it('preserves casing exactly (the backend does not normalize it either)', async () => {
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), 'Control_Puerta_Norte');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() =>
      expect(mockLoginWithEmail).toHaveBeenCalledWith('Control_Puerta_Norte@control.hoydonde.com', 'secret123')
    );
  });

  it('a whitespace-only identifier is rejected by validation, not silently resolved', () => {
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), '   ');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    expect(getByText('El email o usuario es obligatorio')).toBeTruthy();
    expect(mockLoginWithEmail).not.toHaveBeenCalled();
  });

  it('redirects to a safe internal returnTo after login', async () => {
    mockReturnTo = '/(tabs)/explore';
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/(tabs)/explore'));
  });

  it('ignores an unsafe returnTo and falls back to the catalog', async () => {
    mockReturnTo = 'https://evil.example.com';
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/(tabs)'));
  });

  it('shows an inline error on invalid credentials and does not navigate', async () => {
    mockLoginWithEmail.mockRejectedValueOnce({ code: 'auth/wrong-password' });
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com o tu usuario'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'wrong');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(getByText('Email o contraseña incorrectos.')).toBeTruthy());
    expect(mockReplace).not.toHaveBeenCalled();
  });

  describe('"Olvidaste tu contraseña"', () => {
    it('opens the ForgotPasswordModal', () => {
      const { getByText, queryByPlaceholderText } = render(<LoginScreen />);

      expect(queryByPlaceholderText('ejemplo@correo.com')).toBeNull();

      fireEvent.press(getByText('¿Olvidaste tu contraseña?'));

      expect(getByText('Recuperar contraseña')).toBeTruthy();
      expect(getByText('Enviar instrucciones')).toBeTruthy();
    });

    it('closes on Cancelar without calling Firebase', () => {
      const { getByText, queryByText } = render(<LoginScreen />);

      fireEvent.press(getByText('¿Olvidaste tu contraseña?'));
      expect(getByText('Recuperar contraseña')).toBeTruthy();

      fireEvent.press(getByText('Cancelar'));

      expect(queryByText('Recuperar contraseña')).toBeNull();
      expect(mockSendPasswordResetEmail).not.toHaveBeenCalled();
    });
  });
});
