import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

const mockPush = jest.fn();
const mockReplace = jest.fn();
jest.mock('expo-router', () => ({
  router: {
    push: (...args: unknown[]) => mockPush(...args),
    replace: (...args: unknown[]) => mockReplace(...args),
  },
}));

type MockUser = { uid: string; email: string | null; roles: string[]; acciones: string[] } | null;
let mockUser: MockUser = null;
let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({
    user: mockUser,
    initializing: false,
    syncError: null,
    retrySync: jest.fn(),
    logout: jest.fn().mockResolvedValue(undefined),
    hasAccion: (accion: string) => mockHasAccion(accion),
  }),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import ProfileScreen from './explore';

describe('ProfileScreen — panel gated por acciones', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUser = { uid: 'uid-1', email: 'test@hoydonde.com', roles: ['ORGANIZADOR'], acciones: [] };
    mockHasAccion = () => false;
  });

  it('no muestra ninguna entrada del panel sin ninguna acción habilitada (Cliente)', () => {
    mockHasAccion = () => false;
    const { queryByText } = render(<ProfileScreen />);

    expect(queryByText('Administración')).toBeNull();
    expect(queryByText('Organización')).toBeNull();
    expect(queryByText('Control')).toBeNull();
    expect(queryByText('Panel')).toBeNull();
  });

  it('muestra "Administración" solo con USUARIO_CREAR_ORGANIZADOR y navega a /admin', () => {
    mockHasAccion = (accion) => accion === 'USUARIO_CREAR_ORGANIZADOR';
    const { getByText, queryByText } = render(<ProfileScreen />);

    expect(queryByText('Organización')).toBeNull();
    expect(queryByText('Control')).toBeNull();

    fireEvent.press(getByText('Administración'));
    expect(mockPush).toHaveBeenCalledWith('/admin');
  });

  it('muestra "Organización" solo con EVENTO_VER_PROPIOS y navega a /organizer', () => {
    mockHasAccion = (accion) => accion === 'EVENTO_VER_PROPIOS';
    const { getByText, queryByText } = render(<ProfileScreen />);

    expect(queryByText('Administración')).toBeNull();
    expect(queryByText('Control')).toBeNull();

    fireEvent.press(getByText('Organización'));
    expect(mockPush).toHaveBeenCalledWith('/organizer');
  });

  it('muestra "Control" solo con TICKET_VALIDAR y navega a /control', () => {
    mockHasAccion = (accion) => accion === 'TICKET_VALIDAR';
    const { getByText, queryByText } = render(<ProfileScreen />);

    expect(queryByText('Administración')).toBeNull();
    expect(queryByText('Organización')).toBeNull();

    fireEvent.press(getByText('Control'));
    expect(mockPush).toHaveBeenCalledWith('/control');
  });

  it('un Organizador (EVENTO_CREAR sin EVENTO_VER_PROPIOS) no ve la entrada de Organización: el gate es por acción, no por rol', () => {
    mockHasAccion = (accion) => accion === 'EVENTO_CREAR';
    const { queryByText } = render(<ProfileScreen />);

    expect(queryByText('Organización')).toBeNull();
  });

  it('sin sesión no renderiza ningún link del panel', () => {
    mockUser = null;
    mockHasAccion = () => true;
    const { queryByText } = render(<ProfileScreen />);

    expect(queryByText('Administración')).toBeNull();
    expect(queryByText('Organización')).toBeNull();
    expect(queryByText('Control')).toBeNull();
  });
});
