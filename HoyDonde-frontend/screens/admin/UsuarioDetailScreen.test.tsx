import React from 'react';
import { Share } from 'react-native';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

let mockParams: { usuarioId?: string } = { usuarioId: 'usuario-1' };
jest.mock('expo-router', () => ({
  useLocalSearchParams: () => mockParams,
}));

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

const mockListUsuarios = jest.fn();
const mockGetPermisosEfectivos = jest.fn();
const mockListRoles = jest.fn();
const mockListAcciones = jest.fn();
const mockAsignarRol = jest.fn();
const mockQuitarRol = jest.fn();
const mockSetUsuarioActivo = jest.fn();
const mockGenerarPasswordResetLink = jest.fn();
jest.mock('@/services/securityAdminService', () => ({
  securityAdminService: {
    listUsuarios: (...args: unknown[]) => mockListUsuarios(...args),
    getPermisosEfectivos: (...args: unknown[]) => mockGetPermisosEfectivos(...args),
    listRoles: (...args: unknown[]) => mockListRoles(...args),
    listAcciones: (...args: unknown[]) => mockListAcciones(...args),
    asignarRol: (...args: unknown[]) => mockAsignarRol(...args),
    quitarRol: (...args: unknown[]) => mockQuitarRol(...args),
    setUsuarioActivo: (...args: unknown[]) => mockSetUsuarioActivo(...args),
    generarPasswordResetLink: (...args: unknown[]) => mockGenerarPasswordResetLink(...args),
  },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import UsuarioDetailScreen from './UsuarioDetailScreen';

const usuarioResumen = {
  usuarioId: 'usuario-1',
  personaId: 'persona-secreta',
  email: 'organizador@hoydonde.com',
  activo: true,
  rolesActivos: ['ORGANIZADOR'],
};

const permisosOrganizador = {
  usuarioId: 'usuario-1',
  personaId: 'persona-secreta',
  usuarioActivo: true,
  roles: ['ORGANIZADOR'],
  acciones: ['EVENTO_CREAR'],
};

describe('UsuarioDetailScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockParams = { usuarioId: 'usuario-1' };
    mockHasAccion = () => false;
  });

  it('muestra email, roles y permisos efectivos sin exponer usuarioId/personaId', async () => {
    mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
    mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);

    const { findByText, findAllByText, queryByText } = render(<UsuarioDetailScreen />);

    expect(await findByText('organizador@hoydonde.com')).toBeTruthy();
    expect(await findByText('ORGANIZADOR')).toBeTruthy();
    // Sin catálogo de acciones disponible, cae al código como nombre y como código (mismo texto dos veces).
    expect(await findAllByText('EVENTO_CREAR')).toHaveLength(2);
    expect(queryByText('usuario-1')).toBeNull();
    expect(queryByText('persona-secreta')).toBeNull();
  });

  it('muestra el nombre legible de cada acción efectiva cuando el catálogo está disponible', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ASIGNAR_ACCION';
    mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
    mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
    mockListAcciones.mockResolvedValueOnce([{ codigo: 'EVENTO_CREAR', descripcion: 'Crear un evento.', activo: true }]);

    const { findByText } = render(<UsuarioDetailScreen />);

    expect(await findByText('Crear un evento.')).toBeTruthy();
    expect(await findByText('EVENTO_CREAR')).toBeTruthy();
  });

  it('con USUARIO_ASIGNAR_ROL y ROL_EDITAR ofrece asignar un rol no asignado', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_ASIGNAR_ROL' || accion === 'ROL_EDITAR';
    mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
    mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
    mockListRoles.mockResolvedValueOnce([
      { codigo: 'ORGANIZADOR', nombre: 'Organizador', descripcion: '', activo: true, acciones: [] },
      { codigo: 'CONTROL', nombre: 'Control', descripcion: '', activo: true, acciones: [] },
    ]);
    mockAsignarRol.mockResolvedValueOnce(undefined);
    mockGetPermisosEfectivos.mockResolvedValueOnce({ ...permisosOrganizador, roles: ['ORGANIZADOR', 'CONTROL'] });

    const { findByText, getByText } = render(<UsuarioDetailScreen />);
    await findByText('Asignar otro rol');
    expect(await findByText('Control')).toBeTruthy();

    fireEvent.press(getByText('Asignar'));

    await waitFor(() => expect(mockAsignarRol).toHaveBeenCalledWith('usuario-1', 'CONTROL'));
    expect(await findByText('CONTROL')).toBeTruthy();
  });

  it('con USUARIO_QUITAR_ROL, quitar un rol pide confirmación', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_QUITAR_ROL';
    mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
    mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
    mockQuitarRol.mockResolvedValueOnce(undefined);
    mockGetPermisosEfectivos.mockResolvedValueOnce({ ...permisosOrganizador, roles: [], acciones: [] });

    const { findByText, getByText } = render(<UsuarioDetailScreen />);
    await findByText('ORGANIZADOR');

    fireEvent.press(getByText('Quitar'));
    expect(mockQuitarRol).not.toHaveBeenCalled();

    fireEvent.press(getByText('Sí, quitar'));
    await waitFor(() => expect(mockQuitarRol).toHaveBeenCalledWith('usuario-1', 'ORGANIZADOR'));
  });

  it('mapea LAST_ADMINISTRATOR al quitar un rol', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_QUITAR_ROL';
    mockListUsuarios.mockResolvedValueOnce([{ ...usuarioResumen, rolesActivos: ['ADMINISTRADOR'] }]);
    mockGetPermisosEfectivos.mockResolvedValueOnce({ ...permisosOrganizador, roles: ['ADMINISTRADOR'] });
    mockQuitarRol.mockRejectedValueOnce(
      new ApiError({ code: 'LAST_ADMINISTRATOR', message: 'sin administradores', traceId: 't' }, 409)
    );

    const { findByText, getByText } = render(<UsuarioDetailScreen />);
    await findByText('ADMINISTRADOR');

    fireEvent.press(getByText('Quitar'));
    fireEvent.press(getByText('Sí, quitar'));

    expect(await findByText('No se puede quitar: el sistema quedaría sin ningún Administrador efectivo.')).toBeTruthy();
  });

  it('con USUARIO_DESACTIVAR, desactivar la cuenta pide confirmación', async () => {
    mockHasAccion = (accion) => accion === 'USUARIO_DESACTIVAR';
    mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
    mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
    mockSetUsuarioActivo.mockResolvedValueOnce(undefined);

    const { findByText, getByText } = render(<UsuarioDetailScreen />);
    await findByText('organizador@hoydonde.com');

    fireEvent.press(getByText('Desactivar cuenta'));
    expect(mockSetUsuarioActivo).not.toHaveBeenCalled();

    fireEvent.press(getByText('Desactivar'));
    await waitFor(() => expect(mockSetUsuarioActivo).toHaveBeenCalledWith('usuario-1', false));
    expect(await findByText('INACTIVO')).toBeTruthy();
  });

  it('mapea un 403 al ver el usuario', async () => {
    mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
    mockGetPermisosEfectivos.mockRejectedValueOnce(new ApiError({ code: 'FORBIDDEN', message: 'no', traceId: 't' }, 403));

    const { findByText } = render(<UsuarioDetailScreen />);
    expect(await findByText('Tu cuenta no tiene permiso para ver este usuario.')).toBeTruthy();
  });

  it('si el usuario ya no está en la lista, muestra un error de no encontrado', async () => {
    mockListUsuarios.mockResolvedValueOnce([]);
    mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);

    const { findByText } = render(<UsuarioDetailScreen />);
    expect(await findByText('Este usuario ya no existe.')).toBeTruthy();
  });

  describe('Generar enlace de recuperación', () => {
    it('gatea el botón exclusivamente por USUARIO_RESTABLECER_PASSWORD, nunca por rol', async () => {
      mockHasAccion = () => false;
      mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
      mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);

      const { findByText, queryByText } = render(<UsuarioDetailScreen />);
      await findByText('organizador@hoydonde.com');

      expect(queryByText('Generar enlace de recuperación')).toBeNull();
    });

    it('pide confirmación y llama al endpoint con el UsuarioId interno, nunca un UID', async () => {
      mockHasAccion = (accion) => accion === 'USUARIO_RESTABLECER_PASSWORD';
      mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
      mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
      mockGenerarPasswordResetLink.mockResolvedValueOnce({
        resetLink: 'https://firebase.example/reset?oobCode=abc123',
      });

      const { findByText, getByText } = render(<UsuarioDetailScreen />);
      await findByText('organizador@hoydonde.com');

      fireEvent.press(getByText('Generar enlace de recuperación'));
      expect(mockGenerarPasswordResetLink).not.toHaveBeenCalled();

      fireEvent.press(getByText('Generar'));

      await waitFor(() => expect(mockGenerarPasswordResetLink).toHaveBeenCalledWith('usuario-1'));
      expect(await findByText('https://firebase.example/reset?oobCode=abc123')).toBeTruthy();
      expect(
        await findByText(
          'Cualquier persona con este enlace puede elegir una nueva contraseña. Compartilo únicamente con el titular.'
        )
      ).toBeTruthy();
    });

    it('bloquea un doble envío mientras la solicitud está en curso', async () => {
      mockHasAccion = (accion) => accion === 'USUARIO_RESTABLECER_PASSWORD';
      mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
      mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
      let resolveGenerar!: (value: { resetLink: string }) => void;
      mockGenerarPasswordResetLink.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveGenerar = resolve;
        })
      );

      const { findByText, getByText, queryByText } = render(<UsuarioDetailScreen />);
      await findByText('organizador@hoydonde.com');

      fireEvent.press(getByText('Generar enlace de recuperación'));
      fireEvent.press(getByText('Generar'));

      // El botón pasa a estado de carga (sin texto presionable): un segundo toque no puede
      // disparar una segunda solicitud mientras la primera sigue en vuelo.
      expect(queryByText('Generar')).toBeNull();
      expect(mockGenerarPasswordResetLink).toHaveBeenCalledTimes(1);

      resolveGenerar({ resetLink: 'https://firebase.example/reset?oobCode=xyz' });
      await waitFor(() => expect(getByText('https://firebase.example/reset?oobCode=xyz')).toBeTruthy());
      expect(mockGenerarPasswordResetLink).toHaveBeenCalledTimes(1);
    });

    it('permite compartir el enlace con Share.share y descartarlo del estado', async () => {
      mockHasAccion = (accion) => accion === 'USUARIO_RESTABLECER_PASSWORD';
      mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
      mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
      mockGenerarPasswordResetLink.mockResolvedValueOnce({
        resetLink: 'https://firebase.example/reset?oobCode=abc123',
      });
      const shareSpy = jest.spyOn(Share, 'share').mockResolvedValueOnce({ action: 'sharedAction' } as any);

      const { findByText, getByText, queryByText } = render(<UsuarioDetailScreen />);
      await findByText('organizador@hoydonde.com');

      fireEvent.press(getByText('Generar enlace de recuperación'));
      fireEvent.press(getByText('Generar'));
      await findByText('https://firebase.example/reset?oobCode=abc123');

      fireEvent.press(getByText('Compartir'));
      await waitFor(() =>
        expect(shareSpy).toHaveBeenCalledWith({ message: 'https://firebase.example/reset?oobCode=abc123' })
      );

      fireEvent.press(getByText('Descartar'));
      expect(queryByText('https://firebase.example/reset?oobCode=abc123')).toBeNull();
      expect(queryByText('Compartir')).toBeNull();

      shareSpy.mockRestore();
    });

    it('mapea USER_IDENTITY_NOT_RECOVERABLE a un mensaje claro', async () => {
      mockHasAccion = (accion) => accion === 'USUARIO_RESTABLECER_PASSWORD';
      mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
      mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
      mockGenerarPasswordResetLink.mockRejectedValueOnce(
        new ApiError({ code: 'USER_IDENTITY_NOT_RECOVERABLE', message: 'x', traceId: 't' }, 409)
      );

      const { findByText, getByText } = render(<UsuarioDetailScreen />);
      await findByText('organizador@hoydonde.com');

      fireEvent.press(getByText('Generar enlace de recuperación'));
      fireEvent.press(getByText('Generar'));

      expect(await findByText('Este usuario no tiene una identidad de Firebase recuperable.')).toBeTruthy();
    });

    it('el enlace nunca aparece en el árbol al desmontar la pantalla', async () => {
      mockHasAccion = (accion) => accion === 'USUARIO_RESTABLECER_PASSWORD';
      mockListUsuarios.mockResolvedValueOnce([usuarioResumen]);
      mockGetPermisosEfectivos.mockResolvedValueOnce(permisosOrganizador);
      mockGenerarPasswordResetLink.mockResolvedValueOnce({
        resetLink: 'https://firebase.example/reset?oobCode=abc123',
      });

      const { findByText, getByText, unmount } = render(<UsuarioDetailScreen />);
      await findByText('organizador@hoydonde.com');

      fireEvent.press(getByText('Generar enlace de recuperación'));
      fireEvent.press(getByText('Generar'));
      await findByText('https://firebase.example/reset?oobCode=abc123');

      expect(() => unmount()).not.toThrow();
    });
  });
});
