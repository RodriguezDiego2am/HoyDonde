import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';
import { router } from 'expo-router';
import RegisterScreen from './RegisterScreen';

jest.mock('expo-router', () => ({
  router: {
    push: jest.fn(),
    replace: jest.fn(),
  },
}));

const mockRegisterCliente = jest.fn();
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ registerCliente: mockRegisterCliente }),
}));

describe('RegisterScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders correctly', () => {
    const { getByText, getByPlaceholderText } = render(<RegisterScreen />);

    expect(getByText('Crear cuenta')).toBeTruthy();
    expect(getByPlaceholderText('ejemplo@correo.com')).toBeTruthy();
    expect(getByPlaceholderText('Tu contraseña')).toBeTruthy();
    expect(getByPlaceholderText('Confirmá tu contraseña')).toBeTruthy();
    expect(getByPlaceholderText('Tu nombre completo')).toBeTruthy();
    expect(getByPlaceholderText('Tu DNI')).toBeTruthy();
    expect(getByPlaceholderText('Tu número de teléfono')).toBeTruthy();
    expect(getByText('Registrarse')).toBeTruthy();
  });

  it('shows validation errors when the form is submitted empty', () => {
    const { getByText } = render(<RegisterScreen />);

    fireEvent.press(getByText('Registrarse'));

    expect(getByText('El email es obligatorio')).toBeTruthy();
    expect(getByText('La contraseña es obligatoria')).toBeTruthy();
    expect(getByText('El nombre completo es obligatorio')).toBeTruthy();
    expect(getByText('El DNI es obligatorio')).toBeTruthy();
    expect(getByText('El número de teléfono es obligatorio')).toBeTruthy();
    expect(mockRegisterCliente).not.toHaveBeenCalled();
  });

  it('shows an error for an invalid email format', () => {
    const { getByText, getByPlaceholderText } = render(<RegisterScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'invalidemail');
    fireEvent.press(getByText('Registrarse'));

    expect(getByText('Email inválido')).toBeTruthy();
  });

  it('shows an error when passwords do not match', () => {
    const { getByText, getByPlaceholderText } = render(<RegisterScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirmá tu contraseña'), 'different');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');

    fireEvent.press(getByText('Registrarse'));

    expect(getByText('Las contraseñas no coinciden')).toBeTruthy();
  });

  it('calls registerCliente with the Persona data exactly once when the form is valid', async () => {
    mockRegisterCliente.mockResolvedValueOnce(undefined);
    const { getByPlaceholderText, getByText } = render(<RegisterScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirmá tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');

    fireEvent.press(getByText('Registrarse'));

    await waitFor(() => expect(mockRegisterCliente).toHaveBeenCalledTimes(1));
    expect(mockRegisterCliente).toHaveBeenCalledWith({
      email: 'test@example.com',
      password: 'password123',
      fullName: 'John Doe',
      dni: '12345678',
      phoneNumber: '1234567890',
    });
  });

  it('navigates to the catalog after a successful registration', async () => {
    mockRegisterCliente.mockResolvedValueOnce(undefined);
    const { getByPlaceholderText, getByText } = render(<RegisterScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirmá tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');

    fireEvent.press(getByText('Registrarse'));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/(tabs)'));
  });

  it('shows an inline error and stays on the screen when Firebase identity creation fails', async () => {
    mockRegisterCliente.mockRejectedValueOnce({ code: 'auth/email-already-in-use' });
    const { getByPlaceholderText, getByText } = render(<RegisterScreen />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirmá tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');

    fireEvent.press(getByText('Registrarse'));

    await waitFor(() => expect(getByText('Ya existe una cuenta con este email.')).toBeTruthy());
    expect(router.replace).not.toHaveBeenCalled();
  });
});
