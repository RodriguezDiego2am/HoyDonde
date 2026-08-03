import React from 'react';
import { render } from '@testing-library/react-native';

const mockReplace = jest.fn();
let mockSegments: string[] = [];
jest.mock('expo-router', () => ({
  router: { replace: (...args: unknown[]) => mockReplace(...args) },
  useSegments: () => mockSegments,
}));

interface MockAuthState {
  user: { acciones: string[] } | null;
  initializing: boolean;
  syncError: string | null;
}
let mockAuthState: MockAuthState = { user: null, initializing: true, syncError: null };
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => mockAuthState,
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ControlSessionGate } from './ControlSessionGate';

describe('ControlSessionGate', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockSegments = [];
    mockAuthState = { user: null, initializing: true, syncError: null };
  });

  it('no redirige mientras la sesión sigue inicializando', () => {
    mockAuthState = { user: { acciones: ['TICKET_VALIDAR'] }, initializing: true, syncError: null };
    mockSegments = ['(tabs)'];
    render(<ControlSessionGate />);
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('no redirige sin usuario autenticado', () => {
    mockAuthState = { user: null, initializing: false, syncError: null };
    mockSegments = ['(tabs)'];
    render(<ControlSessionGate />);
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('no redirige mientras hay un error de sincronización pendiente', () => {
    mockAuthState = { user: { acciones: ['TICKET_VALIDAR'] }, initializing: false, syncError: 'falló la sincronización' };
    mockSegments = ['(tabs)'];
    render(<ControlSessionGate />);
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('redirige a /control tras login/restauración cuando la cuenta es Control exclusivo', () => {
    mockAuthState = { user: { acciones: ['TICKET_VALIDAR'] }, initializing: false, syncError: null };
    mockSegments = ['(tabs)'];
    render(<ControlSessionGate />);
    expect(mockReplace).toHaveBeenCalledWith('/control');
  });

  it('también redirige desde cualquier otra ruta (no solo (tabs))', () => {
    mockAuthState = { user: { acciones: ['TICKET_VALIDAR'] }, initializing: false, syncError: null };
    mockSegments = ['organizer', 'index'];
    render(<ControlSessionGate />);
    expect(mockReplace).toHaveBeenCalledWith('/control');
  });

  it('no redirige (ni hace loop) si ya está en /control', () => {
    mockAuthState = { user: { acciones: ['TICKET_VALIDAR'] }, initializing: false, syncError: null };
    mockSegments = ['control'];
    render(<ControlSessionGate />);
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('un usuario multirol (TICKET_VALIDAR + otra acción) conserva su navegación: nunca es redirigido', () => {
    mockAuthState = {
      user: { acciones: ['TICKET_VALIDAR', 'EVENTO_VER_PROPIOS'] },
      initializing: false,
      syncError: null,
    };
    mockSegments = ['(tabs)'];
    render(<ControlSessionGate />);
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('un usuario sin TICKET_VALIDAR nunca es redirigido a /control', () => {
    mockAuthState = { user: { acciones: ['TICKET_VER_PROPIO'] }, initializing: false, syncError: null };
    mockSegments = ['(tabs)'];
    render(<ControlSessionGate />);
    expect(mockReplace).not.toHaveBeenCalled();
  });
});
