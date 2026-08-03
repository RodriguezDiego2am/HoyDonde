import React from 'react';
import { act, fireEvent, render, waitFor, within } from '@testing-library/react-native';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args) },
}));

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

// Simula el ciclo de foco de React Navigation: registra el callback más reciente (para que el
// test pueda simular "volver a esta pantalla" invocándolo de nuevo) y lo corre una vez al montar,
// igual que React Navigation hace con la pantalla inicialmente activa.
let mockFocusCallback: (() => void) | null = null;
jest.mock('@react-navigation/native', () => {
  const ReactActual = require('react');
  return {
    useFocusEffect: (callback: () => void) => {
      mockFocusCallback = callback;
      ReactActual.useEffect(() => {
        callback();
      }, []);
    },
  };
});

const mockListRoles = jest.fn();
const mockCreateRol = jest.fn();
jest.mock('@/services/securityAdminService', () => ({
  securityAdminService: {
    listRoles: (...args: unknown[]) => mockListRoles(...args),
    createRol: (...args: unknown[]) => mockCreateRol(...args),
  },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import RolesListScreen from './RolesListScreen';

function rol(overrides: Partial<{ codigo: string; nombre: string; descripcion: string; activo: boolean; acciones: string[] }> = {}) {
  return {
    codigo: 'ORGANIZADOR',
    nombre: 'Organizador',
    descripcion: 'Crea y gestiona sus propios eventos.',
    activo: true,
    acciones: ['EVENTO_CREAR'],
    ...overrides,
  };
}

describe('RolesListScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockHasAccion = () => false;
    mockFocusCallback = null;
  });

  it('lista los roles con su código y estado', async () => {
    mockListRoles.mockResolvedValueOnce([rol(), rol({ codigo: 'CONTROL', nombre: 'Control', activo: false })]);
    const { findByText } = render(<RolesListScreen />);

    expect(await findByText('Organizador')).toBeTruthy();
    expect(await findByText('Control')).toBeTruthy();
    expect(await findByText('ACTIVO')).toBeTruthy();
    expect(await findByText('INACTIVO')).toBeTruthy();
  });

  it('filtra por texto sobre nombre/código/descripción', async () => {
    mockListRoles.mockResolvedValueOnce([rol(), rol({ codigo: 'CONTROL', nombre: 'Control' })]);
    const { findByText, getByPlaceholderText, queryByText } = render(<RolesListScreen />);

    await findByText('Organizador');
    fireEvent.changeText(getByPlaceholderText('Buscar por nombre, código o descripción'), 'control');

    expect(queryByText('Organizador')).toBeNull();
    expect(queryByText('Control')).toBeTruthy();
  });

  it('filtra por estado activo/inactivo', async () => {
    mockListRoles.mockResolvedValueOnce([rol(), rol({ codigo: 'CONTROL', nombre: 'Control', activo: false })]);
    const { findByText, queryByText } = render(<RolesListScreen />);

    await findByText('Organizador');
    fireEvent.press(await findByText('Inactivos'));

    expect(queryByText('Organizador')).toBeNull();
    expect(await findByText('Control')).toBeTruthy();
  });

  it('no muestra "+ Crear rol" sin la acción ROL_CREAR', async () => {
    mockListRoles.mockResolvedValueOnce([]);
    const { findByText, queryByText } = render(<RolesListScreen />);

    await findByText('Todavía no hay roles en el catálogo.');
    expect(queryByText('+ Crear rol')).toBeNull();
  });

  it('crea un rol con ROL_CREAR y lo agrega a la lista sin recargar', async () => {
    mockHasAccion = (accion) => accion === 'ROL_CREAR';
    mockListRoles.mockResolvedValueOnce([]);
    mockCreateRol.mockResolvedValueOnce(rol({ codigo: 'SOPORTE', nombre: 'Soporte', acciones: [] }));

    const { findByText, getByText, getByPlaceholderText } = render(<RolesListScreen />);
    await findByText('Todavía no hay roles en el catálogo.');

    fireEvent.press(getByText('+ Crear rol'));
    fireEvent.changeText(getByPlaceholderText('SOPORTE'), 'soporte');
    fireEvent.changeText(getByPlaceholderText('Soporte'), 'Soporte');
    fireEvent.press(getByText('Crear rol'));

    await waitFor(() =>
      expect(mockCreateRol).toHaveBeenCalledWith({ codigo: 'SOPORTE', nombre: 'Soporte', descripcion: '' })
    );
    expect(await findByText('Soporte')).toBeTruthy();
  });

  it('mapea ROLE_ALREADY_EXISTS a un mensaje de duplicado', async () => {
    mockHasAccion = (accion) => accion === 'ROL_CREAR';
    mockListRoles.mockResolvedValueOnce([]);
    mockCreateRol.mockRejectedValueOnce(new ApiError({ code: 'ROLE_ALREADY_EXISTS', message: 'ya existe', traceId: 't' }, 409));

    const { findByText, getByText, getByPlaceholderText } = render(<RolesListScreen />);
    await findByText('Todavía no hay roles en el catálogo.');

    fireEvent.press(getByText('+ Crear rol'));
    fireEvent.changeText(getByPlaceholderText('SOPORTE'), 'ORGANIZADOR');
    fireEvent.changeText(getByPlaceholderText('Soporte'), 'Organizador');
    fireEvent.press(getByText('Crear rol'));

    expect(await findByText('Ya existe un rol con ese código.')).toBeTruthy();
  });

  it('muestra error con reintento ante una falla de red', async () => {
    mockListRoles.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<RolesListScreen />);

    expect(await findByText('No se pudieron cargar los roles. Verificá tu conexión.')).toBeTruthy();

    mockListRoles.mockResolvedValueOnce([rol()]);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Organizador')).toBeTruthy();
  });

  it('navega al detalle del rol al tocarlo', async () => {
    mockListRoles.mockResolvedValueOnce([rol()]);
    const { findByText } = render(<RolesListScreen />);

    fireEvent.press(await findByText('Organizador'));

    expect(mockPush).toHaveBeenCalledWith({ pathname: '/admin/roles/[codigo]', params: { codigo: 'ORGANIZADOR' } });
  });

  describe('estructura responsive (regresión del overflow de "+ Crear rol")', () => {
    it('búsqueda, filtros de estado y "+ Crear rol" viven en tres filas separadas, nunca combinadas', async () => {
      mockHasAccion = (accion) => accion === 'ROL_CREAR';
      mockListRoles.mockResolvedValueOnce([rol()]);
      const { findByText, getByTestId } = render(<RolesListScreen />);
      await findByText('Organizador');

      const searchRow = getByTestId('roles-search-row');
      const estadoRow = getByTestId('roles-estado-row');
      const createRow = getByTestId('roles-create-row');

      // Cada fila contiene solamente lo suyo: ningún control comparte fila con otro.
      expect(within(searchRow).getByPlaceholderText('Buscar por nombre, código o descripción')).toBeTruthy();
      expect(within(searchRow).queryByText('Todos')).toBeNull();
      expect(within(searchRow).queryByText('+ Crear rol')).toBeNull();

      expect(within(estadoRow).getByText('Todos')).toBeTruthy();
      expect(within(estadoRow).getByText('Activos')).toBeTruthy();
      expect(within(estadoRow).getByText('Inactivos')).toBeTruthy();
      expect(within(estadoRow).queryByText('+ Crear rol')).toBeNull();

      expect(within(createRow).getByText('+ Crear rol')).toBeTruthy();
      expect(within(createRow).queryByText('Todos')).toBeNull();
    });

    it('sin ROL_CREAR no se renderiza la fila de "Crear rol"', async () => {
      mockListRoles.mockResolvedValueOnce([rol()]);
      const { findByText, queryByTestId } = render(<RolesListScreen />);
      await findByText('Organizador');

      expect(queryByTestId('roles-create-row')).toBeNull();
    });
  });

  describe('refresh automático por foco', () => {
    it('recarga automáticamente al recuperar el foco (p. ej. al volver del detalle) y muestra el estado actualizado', async () => {
      mockListRoles.mockResolvedValueOnce([rol({ activo: true })]);
      const { findByText } = render(<RolesListScreen />);

      await findByText('Organizador');
      expect(await findByText('ACTIVO')).toBeTruthy();
      expect(mockListRoles).toHaveBeenCalledTimes(1);

      // El rol se desactivó en su pantalla de detalle; al volver, el foco dispara una recarga.
      mockListRoles.mockResolvedValueOnce([rol({ activo: false })]);
      await act(async () => {
        mockFocusCallback?.();
      });

      expect(mockListRoles).toHaveBeenCalledTimes(2);
      expect(await findByText('INACTIVO')).toBeTruthy();
    });

    it('no borra la lista mientras el refresh de foco está en curso', async () => {
      mockListRoles.mockResolvedValueOnce([rol()]);
      const { findByText, queryByText, getByTestId } = render(<RolesListScreen />);
      await findByText('Organizador');

      let resolveFocusLoad!: (value: unknown) => void;
      mockListRoles.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFocusLoad = resolve;
        })
      );

      act(() => {
        mockFocusCallback?.();
      });

      // Mientras la request de foco sigue pendiente, el rol ya cargado sigue visible: la
      // FlatList real sigue montada, no la reemplazó una pantalla de carga.
      expect(queryByText('Organizador')).toBeTruthy();
      expect(getByTestId('roles-list')).toBeTruthy();

      await act(async () => {
        resolveFocusLoad([rol()]);
      });
    });

    it('no dispara una segunda consulta si ya hay una en vuelo', async () => {
      let resolveInitialLoad!: (value: unknown) => void;
      mockListRoles.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveInitialLoad = resolve;
        })
      );
      render(<RolesListScreen />);

      // Un segundo evento de foco llega antes de que la carga inicial termine.
      act(() => {
        mockFocusCallback?.();
      });
      expect(mockListRoles).toHaveBeenCalledTimes(1);

      await act(async () => {
        resolveInitialLoad([rol()]);
      });
      expect(mockListRoles).toHaveBeenCalledTimes(1);
    });

    it('conserva la búsqueda y el filtro de estado después del refresh por foco', async () => {
      mockListRoles.mockResolvedValueOnce([rol(), rol({ codigo: 'CONTROL', nombre: 'Control', activo: false })]);
      const { findByText, getByPlaceholderText, queryByText } = render(<RolesListScreen />);

      await findByText('Organizador');
      fireEvent.changeText(getByPlaceholderText('Buscar por nombre, código o descripción'), 'control');
      fireEvent.press(await findByText('Inactivos'));
      expect(queryByText('Organizador')).toBeNull();
      expect(getByPlaceholderText('Buscar por nombre, código o descripción').props.value).toBe('control');

      mockListRoles.mockResolvedValueOnce([rol(), rol({ codigo: 'CONTROL', nombre: 'Control', activo: false })]);
      await act(async () => {
        mockFocusCallback?.();
      });

      // El filtro de texto y de estado siguen aplicados tras el refresh silencioso.
      expect(queryByText('Organizador')).toBeNull();
      expect(await findByText('Control')).toBeTruthy();
      expect(getByPlaceholderText('Buscar por nombre, código o descripción').props.value).toBe('control');
    });

    it('un refresh de foco fallido no reemplaza la lista ya visible por una pantalla de error', async () => {
      mockListRoles.mockResolvedValueOnce([rol()]);
      const { findByText, queryByText } = render(<RolesListScreen />);
      await findByText('Organizador');

      mockListRoles.mockRejectedValueOnce(new Error('network down'));
      await act(async () => {
        mockFocusCallback?.();
      });

      expect(queryByText('Organizador')).toBeTruthy();
      expect(queryByText('No se pudieron cargar los roles. Verificá tu conexión.')).toBeNull();
    });
  });
});
