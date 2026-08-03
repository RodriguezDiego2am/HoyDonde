import React from 'react';
import { act, fireEvent, render } from '@testing-library/react-native';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args) },
}));

const mockListUsuarios = jest.fn();
jest.mock('@/services/securityAdminService', () => ({
  securityAdminService: {
    listUsuarios: (...args: unknown[]) => mockListUsuarios(...args),
  },
}));

// Mismo mock de ciclo de foco que RolesListScreen.test.tsx: guarda el callback más reciente para
// que el test pueda simular "volver a esta pantalla" invocándolo de nuevo.
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

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import UsuariosListScreen from './UsuariosListScreen';

function usuario(overrides: Partial<{ usuarioId: string; personaId: string; email: string; activo: boolean; rolesActivos: string[] }> = {}) {
  return {
    usuarioId: 'usuario-1',
    personaId: 'persona-1',
    email: 'organizador@hoydonde.com',
    activo: true,
    rolesActivos: ['ORGANIZADOR'],
    ...overrides,
  };
}

describe('UsuariosListScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockFocusCallback = null;
  });

  it('lista usuarios con email y estado, nunca UsuarioId/PersonaId como texto visible', async () => {
    mockListUsuarios.mockResolvedValueOnce([usuario()]);
    const { findByText, queryByText } = render(<UsuariosListScreen />);

    expect(await findByText('organizador@hoydonde.com')).toBeTruthy();
    expect(await findByText('ACTIVO')).toBeTruthy();
    expect(queryByText('usuario-1')).toBeNull();
    expect(queryByText('persona-1')).toBeNull();
  });

  it('filtra por email', async () => {
    mockListUsuarios.mockResolvedValueOnce([usuario(), usuario({ usuarioId: 'usuario-2', email: 'admin@hoydonde.com', rolesActivos: ['ADMINISTRADOR'] })]);
    const { findByText, getByPlaceholderText, queryByText } = render(<UsuariosListScreen />);

    await findByText('organizador@hoydonde.com');
    fireEvent.changeText(getByPlaceholderText('Buscar por email'), 'admin@');

    expect(queryByText('organizador@hoydonde.com')).toBeNull();
    expect(queryByText('admin@hoydonde.com')).toBeTruthy();
  });

  it('filtra por estado activo/inactivo', async () => {
    mockListUsuarios.mockResolvedValueOnce([usuario(), usuario({ usuarioId: 'usuario-2', email: 'inactivo@hoydonde.com', activo: false })]);
    const { findByText, queryByText } = render(<UsuariosListScreen />);

    await findByText('organizador@hoydonde.com');
    fireEvent.press(await findByText('Inactivos'));

    expect(queryByText('organizador@hoydonde.com')).toBeNull();
    expect(await findByText('inactivo@hoydonde.com')).toBeTruthy();
  });

  it('filtra por rol usando los roles activos ya cargados', async () => {
    mockListUsuarios.mockResolvedValueOnce([
      usuario(),
      usuario({ usuarioId: 'usuario-2', email: 'admin@hoydonde.com', rolesActivos: ['ADMINISTRADOR'] }),
    ]);
    const { findByText, findByLabelText, queryByText } = render(<UsuariosListScreen />);

    await findByText('organizador@hoydonde.com');
    fireEvent.press(await findByLabelText('Filtrar por rol ADMINISTRADOR'));

    expect(queryByText('organizador@hoydonde.com')).toBeNull();
    expect(await findByText('admin@hoydonde.com')).toBeTruthy();
  });

  it('muestra error con reintento ante una falla de red', async () => {
    mockListUsuarios.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<UsuariosListScreen />);

    expect(await findByText('No se pudieron cargar los usuarios. Verificá tu conexión.')).toBeTruthy();

    mockListUsuarios.mockResolvedValueOnce([usuario()]);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('organizador@hoydonde.com')).toBeTruthy();
  });

  it('navega al detalle del usuario al tocarlo', async () => {
    mockListUsuarios.mockResolvedValueOnce([usuario()]);
    const { findByText } = render(<UsuariosListScreen />);

    fireEvent.press(await findByText('organizador@hoydonde.com'));

    expect(mockPush).toHaveBeenCalledWith({ pathname: '/admin/usuarios/[usuarioId]', params: { usuarioId: 'usuario-1' } });
  });

  describe('refresh automático por foco', () => {
    it('recarga automáticamente al recuperar el foco (p. ej. al volver del detalle) y muestra el estado actualizado', async () => {
      mockListUsuarios.mockResolvedValueOnce([usuario({ activo: true })]);
      const { findByText } = render(<UsuariosListScreen />);

      await findByText('organizador@hoydonde.com');
      expect(await findByText('ACTIVO')).toBeTruthy();
      expect(mockListUsuarios).toHaveBeenCalledTimes(1);

      // La cuenta se desactivó en su pantalla de detalle; al volver, el foco dispara una recarga.
      mockListUsuarios.mockResolvedValueOnce([usuario({ activo: false })]);
      await act(async () => {
        mockFocusCallback?.();
      });

      expect(mockListUsuarios).toHaveBeenCalledTimes(2);
      expect(await findByText('INACTIVO')).toBeTruthy();
    });

    it('no borra la lista mientras el refresh de foco está en curso', async () => {
      mockListUsuarios.mockResolvedValueOnce([usuario()]);
      const { findByText, queryByText, getByTestId } = render(<UsuariosListScreen />);
      await findByText('organizador@hoydonde.com');

      let resolveFocusLoad!: (value: unknown) => void;
      mockListUsuarios.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveFocusLoad = resolve;
        })
      );

      act(() => {
        mockFocusCallback?.();
      });

      expect(queryByText('organizador@hoydonde.com')).toBeTruthy();
      expect(getByTestId('usuarios-list')).toBeTruthy();

      await act(async () => {
        resolveFocusLoad([usuario()]);
      });
    });

    it('no dispara una segunda consulta si ya hay una en vuelo', async () => {
      let resolveInitialLoad!: (value: unknown) => void;
      mockListUsuarios.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveInitialLoad = resolve;
        })
      );
      render(<UsuariosListScreen />);

      act(() => {
        mockFocusCallback?.();
      });
      expect(mockListUsuarios).toHaveBeenCalledTimes(1);

      await act(async () => {
        resolveInitialLoad([usuario()]);
      });
      expect(mockListUsuarios).toHaveBeenCalledTimes(1);
    });

    it('conserva la búsqueda, el estado y el filtro de rol después del refresh por foco', async () => {
      const admin = usuario({ usuarioId: 'usuario-2', email: 'admin@hoydonde.com', rolesActivos: ['ADMINISTRADOR'] });
      mockListUsuarios.mockResolvedValueOnce([usuario(), admin]);
      const { findByText, getByPlaceholderText, findByLabelText, queryByText } = render(<UsuariosListScreen />);

      await findByText('organizador@hoydonde.com');
      fireEvent.changeText(getByPlaceholderText('Buscar por email'), 'admin@');
      fireEvent.press(await findByLabelText('Filtrar por rol ADMINISTRADOR'));
      expect(queryByText('organizador@hoydonde.com')).toBeNull();

      mockListUsuarios.mockResolvedValueOnce([usuario(), admin]);
      await act(async () => {
        mockFocusCallback?.();
      });

      expect(queryByText('organizador@hoydonde.com')).toBeNull();
      expect(await findByText('admin@hoydonde.com')).toBeTruthy();
      expect(getByPlaceholderText('Buscar por email').props.value).toBe('admin@');
    });

    it('un refresh de foco fallido no reemplaza la lista ya visible por una pantalla de error', async () => {
      mockListUsuarios.mockResolvedValueOnce([usuario()]);
      const { findByText, queryByText } = render(<UsuariosListScreen />);
      await findByText('organizador@hoydonde.com');

      mockListUsuarios.mockRejectedValueOnce(new Error('network down'));
      await act(async () => {
        mockFocusCallback?.();
      });

      expect(queryByText('organizador@hoydonde.com')).toBeTruthy();
      expect(queryByText('No se pudieron cargar los usuarios. Verificá tu conexión.')).toBeNull();
    });
  });
});
