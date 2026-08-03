import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: { back: (...args: unknown[]) => mockBack(...args) },
}));

const mockRegisterOrganizador = jest.fn();
jest.mock('@/services/userProvisioningService', () => ({
  userProvisioningService: { registerOrganizador: (...args: unknown[]) => mockRegisterOrganizador(...args) },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import CreateOrganizadorScreen from './CreateOrganizadorScreen';

describe('CreateOrganizadorScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('pide únicamente email y contraseña (los campos reales de RegisterOrganizadorDto)', () => {
    const { getByPlaceholderText, getByText, queryByPlaceholderText } = render(<CreateOrganizadorScreen />);

    expect(getByPlaceholderText('organizador@correo.com')).toBeTruthy();
    expect(getByPlaceholderText('Contraseña temporal')).toBeTruthy();
    expect(getByPlaceholderText('Repetí la contraseña')).toBeTruthy();
    expect(getByText('Crear Organizador')).toBeTruthy();
    // Nada de campos personales inexistentes en el DTO real.
    expect(queryByPlaceholderText(/nombre/i)).toBeNull();
    expect(queryByPlaceholderText(/dni/i)).toBeNull();
  });

  it('valida email, contraseña y confirmación antes de enviar', () => {
    const { getByText, getByPlaceholderText } = render(<CreateOrganizadorScreen />);

    fireEvent.press(getByText('Crear Organizador'));
    expect(getByText('El email es obligatorio')).toBeTruthy();
    expect(getByText('La contraseña es obligatoria')).toBeTruthy();
    expect(mockRegisterOrganizador).not.toHaveBeenCalled();

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'no-es-un-email');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), '123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'otra');
    fireEvent.press(getByText('Crear Organizador'));

    expect(getByText('Email inválido')).toBeTruthy();
    expect(getByText('La contraseña debe tener al menos 6 caracteres')).toBeTruthy();
    expect(getByText('Las contraseñas no coinciden')).toBeTruthy();
    expect(mockRegisterOrganizador).not.toHaveBeenCalled();
  });

  it('envía exactamente {email, password} y muestra confirmación sin UID/PersonaId', async () => {
    mockRegisterOrganizador.mockResolvedValueOnce({
      message: 'Organizador creado exitosamente.',
      usuarioId: 'usuario-secreto-123',
      personaId: 'persona-secreta-456',
    });
    const { getByPlaceholderText, getByText, queryByText } = render(<CreateOrganizadorScreen />);

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'nuevo@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Organizador'));

    await waitFor(() =>
      expect(mockRegisterOrganizador).toHaveBeenCalledWith({ email: 'nuevo@hoydonde.com', password: 'segura123' })
    );

    expect(await waitFor(() => getByText('Organizador creado exitosamente.'))).toBeTruthy();
    expect(getByText('nuevo@hoydonde.com')).toBeTruthy();
    expect(queryByText('usuario-secreto-123')).toBeNull();
    expect(queryByText('persona-secreta-456')).toBeNull();
  });

  it('evita el doble envío mientras la solicitud está en curso', async () => {
    let resolveCall!: (value: unknown) => void;
    mockRegisterOrganizador.mockReturnValue(
      new Promise((resolve) => {
        resolveCall = resolve;
      })
    );
    const { getByPlaceholderText, getByText } = render(<CreateOrganizadorScreen />);

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
    mockRegisterOrganizador.mockRejectedValueOnce(
      new ApiError({ code: 'IDENTITY_EMAIL_ALREADY_EXISTS', message: 'ya existe', traceId: 't' }, 409)
    );
    const { getByPlaceholderText, getByText, findByText } = render(<CreateOrganizadorScreen />);

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'repetido@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Organizador'));

    expect(await findByText('Ya existe una cuenta con este email.')).toBeTruthy();
  });

  it('permite volver a crear otro Organizador desde la confirmación', async () => {
    mockRegisterOrganizador.mockResolvedValueOnce({ message: 'ok', usuarioId: 'u', personaId: 'p' });
    const { getByPlaceholderText, getByText, findByText } = render(<CreateOrganizadorScreen />);

    fireEvent.changeText(getByPlaceholderText('organizador@correo.com'), 'nuevo@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Contraseña temporal'), 'segura123');
    fireEvent.changeText(getByPlaceholderText('Repetí la contraseña'), 'segura123');
    fireEvent.press(getByText('Crear Organizador'));

    fireEvent.press(await findByText('Crear otro Organizador'));

    expect(await findByText('Crear Organizador')).toBeTruthy();
    expect(getByPlaceholderText('organizador@correo.com').props.value).toBe('');
  });
});
