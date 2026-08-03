import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args) },
}));

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import AdminHubScreen from './AdminHubScreen';

describe('AdminHubScreen', () => {
  beforeEach(() => {
    mockPush.mockClear();
    mockHasAccion = () => false;
  });

  it('no muestra ninguna entrada sin ninguna acción de administración habilitada', () => {
    const { getByText, queryByText } = render(<AdminHubScreen />);

    expect(queryByText('Altas')).toBeNull();
    expect(queryByText('Roles y acciones')).toBeNull();
    expect(queryByText('Usuarios')).toBeNull();
    expect(getByText('Tu cuenta no tiene acciones de administración habilitadas.')).toBeTruthy();
  });

  it('muestra "Altas" con USUARIO_CREAR_ADMIN o USUARIO_CREAR_ORGANIZADOR y navega a /admin/altas', () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    const { getByText } = render(<AdminHubScreen />);

    fireEvent.press(getByText('Altas'));
    expect(mockPush).toHaveBeenCalledWith('/admin/altas');
  });

  it('muestra "Roles y acciones" solo con ROL_EDITAR y navega a /admin/roles', () => {
    mockHasAccion = (accion) => accion === 'ROL_EDITAR';
    const { getByText, queryByText } = render(<AdminHubScreen />);

    expect(queryByText('Altas')).toBeNull();
    expect(queryByText('Usuarios')).toBeNull();

    fireEvent.press(getByText('Roles y acciones'));
    expect(mockPush).toHaveBeenCalledWith('/admin/roles');
  });

  it('muestra "Usuarios" solo con USUARIO_VER_PERMISOS_EFECTIVOS y navega a /admin/usuarios', () => {
    mockHasAccion = (accion) => accion === 'USUARIO_VER_PERMISOS_EFECTIVOS';
    const { getByText, queryByText } = render(<AdminHubScreen />);

    expect(queryByText('Altas')).toBeNull();
    expect(queryByText('Roles y acciones')).toBeNull();

    fireEvent.press(getByText('Usuarios'));
    expect(mockPush).toHaveBeenCalledWith('/admin/usuarios');
  });

  it('un Administrador con todas las acciones ve las tres entradas', () => {
    mockHasAccion = () => true;
    const { getByText } = render(<AdminHubScreen />);

    expect(getByText('Altas')).toBeTruthy();
    expect(getByText('Roles y acciones')).toBeTruthy();
    expect(getByText('Usuarios')).toBeTruthy();
  });

  it('muestra "Reportes" solo con REPORTE_VER_GLOBAL y navega a /admin/reports', () => {
    mockHasAccion = (accion) => accion === 'REPORTE_VER_GLOBAL';
    const { getByText, queryByText } = render(<AdminHubScreen />);

    expect(queryByText('Altas')).toBeNull();
    expect(queryByText('Usuarios')).toBeNull();

    fireEvent.press(getByText('Reportes'));
    expect(mockPush).toHaveBeenCalledWith('/admin/reports');
  });
});
