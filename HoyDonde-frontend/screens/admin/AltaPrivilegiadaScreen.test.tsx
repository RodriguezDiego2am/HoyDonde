import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: { back: (...args: unknown[]) => mockBack(...args) },
}));

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

const mockRegisterAdmin = jest.fn();
const mockRegisterOrganizador = jest.fn();
jest.mock('@/services/userProvisioningService', () => ({
  userProvisioningService: {
    registerAdmin: (...args: unknown[]) => mockRegisterAdmin(...args),
    registerOrganizador: (...args: unknown[]) => mockRegisterOrganizador(...args),
  },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import AltaPrivilegiadaScreen from './AltaPrivilegiadaScreen';

describe('AltaPrivilegiadaScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockHasAccion = () => false;
  });

  it('sin ninguna acción de alta habilitada, muestra un bloqueo y no ningún formulario', () => {
    const { getByText, queryByPlaceholderText } = render(<AltaPrivilegiadaScreen />);

    expect(getByText('Tu cuenta no tiene acciones habilitadas para dar altas.')).toBeTruthy();
    expect(queryByPlaceholderText('admin@correo.com')).toBeNull();
    expect(queryByPlaceholderText('organizador@correo.com')).toBeNull();
  });

  it('con una sola acción habilitada, va directo al formulario sin selector de tipo', () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    const { getByPlaceholderText, queryByText } = render(<AltaPrivilegiadaScreen />);

    expect(getByPlaceholderText('organizador@correo.com')).toBeTruthy();
    expect(queryByText('Administrador')).toBeNull();
  });

  it('con ambas acciones habilitadas, muestra el selector y cambia el formulario', () => {
    mockHasAccion = () => true;
    const { getByText, getByPlaceholderText } = render(<AltaPrivilegiadaScreen />);

    expect(getByPlaceholderText('admin@correo.com')).toBeTruthy();

    fireEvent.press(getByText('Organizador'));
    expect(getByPlaceholderText('organizador@correo.com')).toBeTruthy();
  });

  it('envía exactamente {email, password} a registerAdmin y no muestra usuarioId/personaId', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ADMIN';
    mockRegisterAdmin.mockResolvedValueOnce({
      message: 'Administrador creado exitosamente.',
      usuarioId: 'usuario-secreto',
      personaId: 'persona-secreta',
    });
    const { getByPlaceholderText, getByText, queryByText } = render(<AltaPrivilegiadaScreen />);

    fireEvent.changeText(getByPlaceholderText('admin@correo.com'), 'nuevo-admin@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Administrador'));

    await waitFor(() =>
      expect(mockRegisterAdmin).toHaveBeenCalledWith({ email: 'nuevo-admin@hoydonde.com', password: 'segura123' })
    );
    expect(await waitFor(() => getByText('Administrador creado exitosamente.'))).toBeTruthy();
    expect(queryByText('usuario-secreto')).toBeNull();
    expect(queryByText('persona-secreta')).toBeNull();
  });

  it('envía exactamente {email, password} a registerOrganizador', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    mockRegisterOrganizador.mockResolvedValueOnce({ message: 'ok', usuarioId: 'u', personaId: 'p' });
    const { getByPlaceholderText, getByText } = render(<AltaPrivilegiadaScreen />);

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'nuevo@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Organizador'));

    await waitFor(() =>
      expect(mockRegisterOrganizador).toHaveBeenCalledWith({ email: 'nuevo@hoydonde.com', password: 'segura123' })
    );
  });

  it('valida email, contraseña y confirmación antes de enviar', () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    const { getByText } = render(<AltaPrivilegiadaScreen />);

    fireEvent.press(getByText('Crear Organizador'));

    expect(getByText('El email es obligatorio')).toBeTruthy();
    expect(getByText('La contraseña es obligatoria')).toBeTruthy();
    expect(mockRegisterOrganizador).not.toHaveBeenCalled();
  });

  it('evita el doble envío mientras la solicitud está en curso', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    let resolveCall!: (value: unknown) => void;
    mockRegisterOrganizador.mockReturnValue(
      new Promise((resolve) => {
        resolveCall = resolve;
      })
    );
    const { getByPlaceholderText, getByText } = render(<AltaPrivilegiadaScreen />);

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'nuevo@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');

    const submitButton = getByText('Crear Organizador');
    fireEvent.press(submitButton);
    fireEvent.press(submitButton);
    fireEvent.press(submitButton);

    expect(mockRegisterOrganizador).toHaveBeenCalledTimes(1);
    resolveCall({ message: 'ok', usuarioId: 'u', personaId: 'p' });
    await waitFor(() => expect(mockRegisterOrganizador).toHaveBeenCalledTimes(1));
  });

  it('mapea IDENTITY_EMAIL_ALREADY_EXISTS a un mensaje claro', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    mockRegisterOrganizador.mockRejectedValueOnce(
      new ApiError({ code: 'IDENTITY_EMAIL_ALREADY_EXISTS', message: 'ya existe', traceId: 't' }, 409)
    );
    const { getByPlaceholderText, getByText, findByText } = render(<AltaPrivilegiadaScreen />);

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'repetido@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Organizador'));

    expect(await findByText('Ya existe una cuenta con este email.')).toBeTruthy();
  });

  it('mapea un 403 a un mensaje de permisos específico del tipo', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ADMIN';
    mockRegisterAdmin.mockRejectedValueOnce(new ApiError({ code: 'FORBIDDEN', message: 'no', traceId: 't' }, 403));
    const { getByPlaceholderText, getByText, findByText } = render(<AltaPrivilegiadaScreen />);

    fireEvent.changeText(getByPlaceholderText('admin@correo.com'), 'nuevo@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Administrador'));

    expect(await findByText('Tu cuenta no tiene permiso para crear cuentas de Administrador.')).toBeTruthy();
  });
});
