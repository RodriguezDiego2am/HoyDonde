import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockPush = jest.fn();
const mockReplace = jest.fn();
jest.mock('expo-router', () => ({
  router: {
    push: (...args: unknown[]) => mockPush(...args),
    replace: (...args: unknown[]) => mockReplace(...args),
  },
}));

const mockLogout = jest.fn().mockResolvedValue(undefined);
let mockUser: { email: string | null } | null = { email: 'control_1@control.hoydonde.com' };
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser, logout: (...args: unknown[]) => mockLogout(...args) }),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import ControlHubScreen from './ControlHubScreen';

describe('ControlHubScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUser = { email: 'control_1@control.hoydonde.com' };
  });

  it('muestra el nombre de usuario derivado, nunca el email sintético completo', () => {
    const { getByText, queryByText } = render(<ControlHubScreen />);

    expect(getByText('CONTROL 1')).toBeTruthy();
    expect(queryByText('control_1@control.hoydonde.com')).toBeNull();
    expect(queryByText(/@control\.hoydonde\.com/)).toBeNull();
  });

  it('muestra el título "Control de acceso"', () => {
    const { getByText } = render(<ControlHubScreen />);
    expect(getByText('Control de acceso')).toBeTruthy();
  });

  it('"Escanear entrada" navega a /control/scan', () => {
    const { getByText } = render(<ControlHubScreen />);
    fireEvent.press(getByText('Escanear entrada'));
    expect(mockPush).toHaveBeenCalledWith('/control/scan');
  });

  it('"Ingresar código manualmente" navega a /control/manual', () => {
    const { getByText } = render(<ControlHubScreen />);
    fireEvent.press(getByText('Ingresar código manualmente'));
    expect(mockPush).toHaveBeenCalledWith('/control/manual');
  });

  it('"Cerrar sesión" pide confirmación antes de cerrar la sesión', async () => {
    const { getByText, queryByText } = render(<ControlHubScreen />);

    // Antes de confirmar, todavía no se llamó a logout.
    expect(queryByText('Sí, cerrar sesión')).toBeNull();
    fireEvent.press(getByText('Cerrar sesión'));
    expect(mockLogout).not.toHaveBeenCalled();

    fireEvent.press(getByText('Sí, cerrar sesión'));

    await waitFor(() => expect(mockLogout).toHaveBeenCalledTimes(1));
    expect(mockReplace).toHaveBeenCalledWith('/(tabs)');
  });

  it('"Cancelar" en el diálogo no cierra la sesión', () => {
    const { getByText, queryByText } = render(<ControlHubScreen />);

    fireEvent.press(getByText('Cerrar sesión'));
    fireEvent.press(getByText('Cancelar'));

    expect(mockLogout).not.toHaveBeenCalled();
    expect(queryByText('Sí, cerrar sesión')).toBeNull();
  });

  it('no muestra Cartelera, Mis entradas ni Perfil como navegación', () => {
    const { queryByText } = render(<ControlHubScreen />);
    expect(queryByText('Cartelera')).toBeNull();
    expect(queryByText('Mis entradas')).toBeNull();
    expect(queryByText('Perfil')).toBeNull();
  });
});
