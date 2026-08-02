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

describe('LoginScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockReturnTo = undefined;
  });

  it('shows validation errors on empty submit and does not call loginWithEmail', () => {
    const { getByText } = render(<LoginScreen />);

    fireEvent.press(getByText('Ingresar'));

    expect(getByText('El email es obligatorio')).toBeTruthy();
    expect(getByText('La contraseña es obligatoria')).toBeTruthy();
    expect(mockLoginWithEmail).not.toHaveBeenCalled();
  });

  it('logs in and redirects to the catalog when there is no returnTo', async () => {
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(mockLoginWithEmail).toHaveBeenCalledWith('cliente@hoydonde.com', 'secret123'));
    expect(mockReplace).toHaveBeenCalledWith('/(tabs)');
  });

  it('redirects to a safe internal returnTo after login', async () => {
    mockReturnTo = '/(tabs)/explore';
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/(tabs)/explore'));
  });

  it('ignores an unsafe returnTo and falls back to the catalog', async () => {
    mockReturnTo = 'https://evil.example.com';
    mockLoginWithEmail.mockResolvedValueOnce(undefined);
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'secret123');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/(tabs)'));
  });

  it('shows an inline error on invalid credentials and does not navigate', async () => {
    mockLoginWithEmail.mockRejectedValueOnce({ code: 'auth/wrong-password' });
    const { getByText, getByPlaceholderText } = render(<LoginScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'wrong');
    fireEvent.press(getByText('Ingresar'));

    await waitFor(() => expect(getByText('Email o contraseña incorrectos.')).toBeTruthy());
    expect(mockReplace).not.toHaveBeenCalled();
  });
});
