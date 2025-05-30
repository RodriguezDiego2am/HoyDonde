import React from 'react';
import { render, fireEvent, waitFor } from '@testing-library/react-native';
import RegisterScreen from './RegisterScreen';
import { Alert } from 'react-native';

// Mock complete dependencies
jest.mock('expo-router', () => ({
  router: {
    push: jest.fn(),
    replace: jest.fn(),
  },
}));

// Mock SecureStore
jest.mock('expo-secure-store', () => ({
  setItemAsync: jest.fn(() => Promise.resolve()),
  getItemAsync: jest.fn(() => Promise.resolve(null)),
  deleteItemAsync: jest.fn(() => Promise.resolve()),
}));

// Mock axios and API completely
jest.mock('axios', () => ({
  create: jest.fn(() => ({
    interceptors: {
      request: { use: jest.fn() },
      response: { use: jest.fn() },
    },
    post: jest.fn(),
    get: jest.fn(),
  })),
}));

// Mock complete APIService module
jest.mock('../services/APIService', () => {
  const originalModule = jest.requireActual('../services/APIService');
  
  return {
    ...originalModule,
    authService: {
      registerCliente: jest.fn(() => Promise.resolve({ data: { success: true } })),
      login: jest.fn(),
      logout: jest.fn(),
    },
    storeToken: jest.fn(),
    storeUser: jest.fn(),
    getToken: jest.fn(() => Promise.resolve(null)),
    getUser: jest.fn(() => Promise.resolve(null)),
    clearStorage: jest.fn(),
  };
});

// Mock Alert
jest.spyOn(Alert, 'alert').mockImplementation((title, message, buttons) => {
  if (buttons && buttons[0] && buttons[0].onPress) {
    buttons[0].onPress();
  }
  return null;
});

describe('RegisterScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders correctly', () => {
    const { getByText, getByPlaceholderText } = render(<RegisterScreen />);
    
    // Check if title and inputs are displayed
    expect(getByText('Crear una cuenta')).toBeTruthy();
    expect(getByPlaceholderText('ejemplo@correo.com')).toBeTruthy();
    expect(getByPlaceholderText('Tu contraseña')).toBeTruthy();
    expect(getByPlaceholderText('Confirma tu contraseña')).toBeTruthy();
    expect(getByPlaceholderText('Tu nombre completo')).toBeTruthy();
    expect(getByPlaceholderText('Tu DNI')).toBeTruthy();
    expect(getByPlaceholderText('Tu número de teléfono')).toBeTruthy();
    expect(getByText('Registrarse')).toBeTruthy();
  });

  it('shows validation errors when form is submitted with empty fields', () => {
    const { getByText } = render(<RegisterScreen />);
    
    // Submit the form without filling any field
    fireEvent.press(getByText('Registrarse'));
    
    // Check for validation error messages
    expect(getByText('El email es obligatorio')).toBeTruthy();
    expect(getByText('La contraseña es obligatoria')).toBeTruthy();
    expect(getByText('El nombre completo es obligatorio')).toBeTruthy();
    expect(getByText('El DNI es obligatorio')).toBeTruthy();
    expect(getByText('El número de teléfono es obligatorio')).toBeTruthy();
  });

  it('shows error for invalid email format', () => {
    const { getByText, getByPlaceholderText } = render(<RegisterScreen />);
    
    // Fill in invalid email
    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'invalidemail');
    
    // Submit the form
    fireEvent.press(getByText('Registrarse'));
    
    // Check for email validation error
    expect(getByText('Email inválido')).toBeTruthy();
  });

  it('shows error when passwords do not match', () => {
    const { getByText, getByPlaceholderText } = render(<RegisterScreen />);
    
    // Fill in all required fields
    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirma tu contraseña'), 'different');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');
    
    // Submit the form
    fireEvent.press(getByText('Registrarse'));
    
    // Check for password match error
    expect(getByText('Las contraseñas no coinciden')).toBeTruthy();
  });

  it('calls registerCliente with correct data when form is valid', async () => {
    const { authService } = require('../services/APIService');
    const { getByPlaceholderText, getByText } = render(<RegisterScreen />);
    
    // Fill in all required fields with valid data
    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirma tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');
    
    // Use a more direct approach for the button press
    const button = getByText('Registrarse');
    button.props.onPress();
    
    // Check if registerCliente was called with correct data
    await waitFor(() => {
      expect(authService.registerCliente).toHaveBeenCalledWith({
        email: 'test@example.com',
        password: 'password123',
        fullName: 'John Doe',
        dni: '12345678',
        phoneNumber: '1234567890'
      });
    }, { timeout: 3000 });
  });

  it('shows alert on registration error', async () => {
    const { authService } = require('../services/APIService');
    
    // Mock the API to throw an error
    authService.registerCliente.mockRejectedValueOnce({
      response: {
        data: 'Email already in use'
      }
    });
    
    const { getByPlaceholderText, getByText } = render(<RegisterScreen />);
    
    // Fill in all required fields
    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirma tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');
    
    // Use a more direct approach for the button press
    const button = getByText('Registrarse');
    button.props.onPress();
    
    // Check if alert was shown with error message
    await waitFor(() => {
      expect(Alert.alert).toHaveBeenCalledWith('Error', 'Email already in use');
    }, { timeout: 3000 });
  });

  it('navigates to login screen after successful registration', async () => {
    const { router } = require('expo-router');
    const { getByPlaceholderText, getByText } = render(<RegisterScreen />);
    
    // Fill in all required fields with valid data
    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'test@example.com');
    fireEvent.changeText(getByPlaceholderText('Tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Confirma tu contraseña'), 'password123');
    fireEvent.changeText(getByPlaceholderText('Tu nombre completo'), 'John Doe');
    fireEvent.changeText(getByPlaceholderText('Tu DNI'), '12345678');
    fireEvent.changeText(getByPlaceholderText('Tu número de teléfono'), '1234567890');
    
    // Use a more direct approach for the button press
    const button = getByText('Registrarse');
    button.props.onPress();
    
    // Check if alert was shown and navigation occurred
    await waitFor(() => {
      expect(Alert.alert).toHaveBeenCalledWith(
        'Registro exitoso',
        'Tu cuenta ha sido creada correctamente',
        expect.any(Array)
      );
      expect(router.push).toHaveBeenCalledWith('/login');
    }, { timeout: 3000 });
  });
});