import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

let mockParams: { codigo?: string } = { codigo: 'ORGANIZADOR' };
const mockRouterReplace = jest.fn();
jest.mock('expo-router', () => ({
  useLocalSearchParams: () => mockParams,
  router: { replace: (...args: unknown[]) => mockRouterReplace(...args) },
}));

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

const mockGetRol = jest.fn();
const mockUpdateRol = jest.fn();
const mockSetRolActivo = jest.fn();
const mockListAcciones = jest.fn();
const mockAsignarAccion = jest.fn();
const mockQuitarAccion = jest.fn();
const mockDeleteRol = jest.fn();
jest.mock('@/services/securityAdminService', () => ({
  securityAdminService: {
    getRol: (...args: unknown[]) => mockGetRol(...args),
    updateRol: (...args: unknown[]) => mockUpdateRol(...args),
    setRolActivo: (...args: unknown[]) => mockSetRolActivo(...args),
    listAcciones: (...args: unknown[]) => mockListAcciones(...args),
    asignarAccion: (...args: unknown[]) => mockAsignarAccion(...args),
    quitarAccion: (...args: unknown[]) => mockQuitarAccion(...args),
    deleteRol: (...args: unknown[]) => mockDeleteRol(...args),
  },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import RolDetailScreen from './RolDetailScreen';

function rolOrganizador(overrides: Partial<{ activo: boolean; acciones: string[] }> = {}) {
  return {
    codigo: 'ORGANIZADOR',
    nombre: 'Organizador',
    descripcion: 'Crea y gestiona sus propios eventos.',
    activo: true,
    acciones: ['EVENTO_CREAR'],
    ...overrides,
  };
}

function rolPersonalizado(overrides: Partial<{ activo: boolean; acciones: string[] }> = {}) {
  return {
    codigo: 'SOPORTE',
    nombre: 'Soporte',
    descripcion: 'Atiende reclamos.',
    activo: false,
    acciones: [],
    ...overrides,
  };
}

const catalogoAcciones = [
  { codigo: 'EVENTO_CREAR', descripcion: 'Crear un evento.', activo: true },
  { codigo: 'EVENTO_EDITAR_PROPIO', descripcion: 'Editar un evento propio.', activo: true },
  { codigo: 'USUARIO_CREAR_ADMIN', descripcion: 'Crear una cuenta de Administrador.', activo: true },
];

describe('RolDetailScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockParams = { codigo: 'ORGANIZADOR' };
    mockHasAccion = () => false;
  });

  it('muestra nombre, código y descripción del rol', async () => {
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    const { findByText } = render(<RolDetailScreen />);

    expect(await findByText('Organizador')).toBeTruthy();
    expect(await findByText('ORGANIZADOR')).toBeTruthy();
  });

  it('sin ROL_EDITAR no muestra el formulario de edición', async () => {
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    const { findByText, queryByPlaceholderText } = render(<RolDetailScreen />);

    await findByText('Organizador');
    expect(queryByPlaceholderText('Nombre del rol')).toBeNull();
  });

  it('con ROL_EDITAR permite guardar nombre/descripción', async () => {
    mockHasAccion = (accion) => accion === 'ROL_EDITAR';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockUpdateRol.mockResolvedValueOnce(rolOrganizador());
    const { findByText, getByDisplayValue, getByText } = render(<RolDetailScreen />);

    await findByText('Organizador');
    fireEvent.changeText(getByDisplayValue('Organizador'), 'Organizador de eventos');
    fireEvent.press(getByText('Guardar cambios'));

    await waitFor(() =>
      expect(mockUpdateRol).toHaveBeenCalledWith('ORGANIZADOR', {
        nombre: 'Organizador de eventos',
        descripcion: 'Crea y gestiona sus propios eventos.',
      })
    );
  });

  it('con ROL_ACTIVAR, dar de baja lógica pide confirmación', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ACTIVAR';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockSetRolActivo.mockResolvedValueOnce(undefined);
    const { findByText, getByText } = render(<RolDetailScreen />);

    await findByText('Organizador');
    fireEvent.press(getByText('Dar de baja lógica'));
    expect(mockSetRolActivo).not.toHaveBeenCalled();

    fireEvent.press(getByText('Dar de baja'));
    await waitFor(() => expect(mockSetRolActivo).toHaveBeenCalledWith('ORGANIZADOR', false));
    expect(await findByText('INACTIVO')).toBeTruthy();
  });

  it('mapea LAST_ADMINISTRATOR a un mensaje claro al desactivar', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ACTIVAR';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockSetRolActivo.mockRejectedValueOnce(
      new ApiError({ code: 'LAST_ADMINISTRATOR', message: 'sin administradores', traceId: 't' }, 409)
    );
    const { findByText, getByText } = render(<RolDetailScreen />);

    await findByText('Organizador');
    fireEvent.press(getByText('Dar de baja lógica'));
    fireEvent.press(getByText('Dar de baja'));

    expect(await findByText('No se puede desactivar: el sistema quedaría sin ningún Administrador efectivo.')).toBeTruthy();
  });

  it('con ROL_ASIGNAR_ACCION carga el catálogo agrupado por dominio con nombre legible y código', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ASIGNAR_ACCION';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockListAcciones.mockResolvedValueOnce(catalogoAcciones);

    const { findByText } = render(<RolDetailScreen />);

    expect(await findByText('EVENTO')).toBeTruthy();
    expect(await findByText('Crear un evento.')).toBeTruthy();
    expect(await findByText('EVENTO_CREAR')).toBeTruthy();
    expect(await findByText('USUARIO')).toBeTruthy();
    expect(await findByText('Crear una cuenta de Administrador.')).toBeTruthy();
  });

  it('asigna una acción no asignada sin pedir confirmación', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ASIGNAR_ACCION';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockListAcciones.mockResolvedValueOnce(catalogoAcciones);
    mockAsignarAccion.mockResolvedValueOnce(undefined);

    const { findByLabelText } = render(<RolDetailScreen />);
    const asignarBoton = await findByLabelText('Asignar Editar un evento propio.');

    fireEvent.press(asignarBoton);

    await waitFor(() => expect(mockAsignarAccion).toHaveBeenCalledWith('ORGANIZADOR', 'EVENTO_EDITAR_PROPIO'));
  });

  it('quitar una acción asignada pide confirmación', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ASIGNAR_ACCION' || accion === 'ROL_QUITAR_ACCION';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockListAcciones.mockResolvedValueOnce(catalogoAcciones);
    mockQuitarAccion.mockResolvedValueOnce(undefined);

    const { findByLabelText, getByText } = render(<RolDetailScreen />);
    const quitarBoton = await findByLabelText('Quitar Crear un evento.');

    fireEvent.press(quitarBoton);
    expect(mockQuitarAccion).not.toHaveBeenCalled();

    fireEvent.press(getByText('Quitar'));
    await waitFor(() => expect(mockQuitarAccion).toHaveBeenCalledWith('ORGANIZADOR', 'EVENTO_CREAR'));
  });

  it('mapea un 403 al modificar acciones', async () => {
    mockHasAccion = (accion) => accion === 'ROL_ASIGNAR_ACCION';
    mockGetRol.mockResolvedValueOnce(rolOrganizador());
    mockListAcciones.mockResolvedValueOnce(catalogoAcciones);
    mockAsignarAccion.mockRejectedValueOnce(new ApiError({ code: 'FORBIDDEN', message: 'no', traceId: 't' }, 403));

    const { findByLabelText, findByText } = render(<RolDetailScreen />);
    fireEvent.press(await findByLabelText('Asignar Editar un evento propio.'));

    expect(await findByText('No tenés permiso para modificar las acciones de este rol.')).toBeTruthy();
  });

  it('muestra error con reintento si falla la carga del rol', async () => {
    mockGetRol.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<RolDetailScreen />);

    expect(await findByText('No se pudo cargar el rol. Verificá tu conexión.')).toBeTruthy();
  });

  // ---- Baja física (docs/api-mvp-plan.md §12) ----

  describe('baja física', () => {
    beforeEach(() => {
      mockRouterReplace.mockClear();
    });

    it('sin ROL_ELIMINAR no muestra la zona de peligro', async () => {
      mockHasAccion = () => false;
      mockGetRol.mockResolvedValueOnce(rolPersonalizado());
      const { findByText, queryByText, queryAllByText } = render(<RolDetailScreen />);

      await findByText('Soporte');
      expect(queryByText('Zona de peligro')).toBeNull();
      expect(queryAllByText('Eliminar definitivamente')).toHaveLength(0);
    });

    it('con ROL_ELIMINAR, un rol esencial muestra la nota de protegido y nunca el botón de eliminar', async () => {
      mockParams = { codigo: 'ORGANIZADOR' };
      mockHasAccion = (accion) => accion === 'ROL_ELIMINAR';
      mockGetRol.mockResolvedValueOnce(rolOrganizador({ activo: false }));
      const { findByText, queryAllByText } = render(<RolDetailScreen />);

      await findByText('Organizador');
      expect(await findByText('Este es un rol esencial del sistema y nunca puede eliminarse físicamente.')).toBeTruthy();
      expect(queryAllByText('Eliminar definitivamente')).toHaveLength(0);
    });

    it('con ROL_ELIMINAR, un rol personalizado activo indica que primero hay que darlo de baja lógica', async () => {
      mockParams = { codigo: 'SOPORTE' };
      mockHasAccion = (accion) => accion === 'ROL_ELIMINAR';
      mockGetRol.mockResolvedValueOnce(rolPersonalizado({ activo: true }));
      const { findByText, queryAllByText } = render(<RolDetailScreen />);

      await findByText('Soporte');
      expect(await findByText('Para eliminar este rol de forma definitiva, primero dalo de baja lógica.')).toBeTruthy();
      expect(queryAllByText('Eliminar definitivamente')).toHaveLength(0);
    });

    it('con ROL_ELIMINAR, un rol personalizado inactivo muestra "Eliminar definitivamente" y pide confirmación', async () => {
      mockParams = { codigo: 'SOPORTE' };
      mockHasAccion = (accion) => accion === 'ROL_ELIMINAR';
      mockGetRol.mockResolvedValueOnce(rolPersonalizado({ activo: false }));
      mockDeleteRol.mockResolvedValueOnce(undefined);
      const { findByText, getAllByText } = render(<RolDetailScreen />);

      await findByText('Soporte');
      // El ConfirmDialog ya está montado en el árbol (oculto vía Modal visible=false), así que
      // "Eliminar definitivamente" siempre matchea 2 elementos: el botón que abre la
      // confirmación (el primero) y el botón de confirmar dentro del diálogo (el último).
      fireEvent.press(getAllByText('Eliminar definitivamente')[0]);
      expect(mockDeleteRol).not.toHaveBeenCalled();

      const botones = getAllByText('Eliminar definitivamente');
      fireEvent.press(botones[botones.length - 1]);

      await waitFor(() => expect(mockDeleteRol).toHaveBeenCalledWith('SOPORTE'));
      await waitFor(() => expect(mockRouterReplace).toHaveBeenCalledWith('/admin/roles'));
    });

    it('impide un doble envío mientras la eliminación está en curso', async () => {
      mockParams = { codigo: 'SOPORTE' };
      mockHasAccion = (accion) => accion === 'ROL_ELIMINAR';
      mockGetRol.mockResolvedValueOnce(rolPersonalizado({ activo: false }));
      let resolveDelete: (() => void) | undefined;
      mockDeleteRol.mockReturnValueOnce(new Promise<void>((resolve) => { resolveDelete = resolve; }));
      const { findByText, getAllByText } = render(<RolDetailScreen />);

      await findByText('Soporte');
      fireEvent.press(getAllByText('Eliminar definitivamente')[0]);
      const confirmar = getAllByText('Eliminar definitivamente').slice(-1)[0];
      fireEvent.press(confirmar);
      fireEvent.press(confirmar);

      await waitFor(() => expect(mockDeleteRol).toHaveBeenCalledTimes(1));
      resolveDelete?.();
    });

    it('mapea ROL_TIENE_USUARIOS_ASIGNADOS a un mensaje claro', async () => {
      mockParams = { codigo: 'SOPORTE' };
      mockHasAccion = (accion) => accion === 'ROL_ELIMINAR';
      mockGetRol.mockResolvedValueOnce(rolPersonalizado({ activo: false }));
      mockDeleteRol.mockRejectedValueOnce(
        new ApiError({ code: 'ROL_TIENE_USUARIOS_ASIGNADOS', message: 'tiene usuarios', traceId: 't' }, 409)
      );
      const { findByText, getAllByText } = render(<RolDetailScreen />);

      await findByText('Soporte');
      fireEvent.press(getAllByText('Eliminar definitivamente')[0]);
      fireEvent.press(getAllByText('Eliminar definitivamente').slice(-1)[0]);

      expect(await findByText('El rol tiene usuarios asignados y no puede eliminarse.')).toBeTruthy();
      expect(mockRouterReplace).not.toHaveBeenCalled();
    });

    it('mapea ROLE_NOT_FOUND (404) a un mensaje claro', async () => {
      mockParams = { codigo: 'SOPORTE' };
      mockHasAccion = (accion) => accion === 'ROL_ELIMINAR';
      mockGetRol.mockResolvedValueOnce(rolPersonalizado({ activo: false }));
      mockDeleteRol.mockRejectedValueOnce(
        new ApiError({ code: 'ROLE_NOT_FOUND', message: 'no existe', traceId: 't' }, 404)
      );
      const { findByText, getAllByText } = render(<RolDetailScreen />);

      await findByText('Soporte');
      fireEvent.press(getAllByText('Eliminar definitivamente')[0]);
      fireEvent.press(getAllByText('Eliminar definitivamente').slice(-1)[0]);

      expect(await findByText('Este rol ya no existe.')).toBeTruthy();
    });
  });
});
