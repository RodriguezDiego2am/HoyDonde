import React from 'react';
import { render } from '@testing-library/react-native';

type CapturedScreen = { name: string; href?: unknown };
let mockCapturedScreens: CapturedScreen[] = [];

jest.mock('expo-router', () => {
  const ReactActual = require('react');
  function Tabs({ children }: { children: React.ReactNode }) {
    return ReactActual.createElement(ReactActual.Fragment, null, children);
  }
  Tabs.Screen = function Screen({ name, options }: { name: string; options?: { href?: unknown } }) {
    mockCapturedScreens.push({ name, href: options?.href });
    return null;
  };
  return { Tabs };
});

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import TabLayout from './_layout';

describe('TabLayout — visibilidad de "Mis entradas"', () => {
  beforeEach(() => {
    mockCapturedScreens = [];
  });

  it('oculta la pestaña (href: null) cuando el usuario no tiene TICKET_VER_PROPIO, sin importar su rol', () => {
    mockHasAccion = (accion) => accion === 'EVENTO_CREAR'; // Organizador, no Cliente: nunca por nombre de rol
    render(<TabLayout />);

    const tickets = mockCapturedScreens.find((s) => s.name === 'tickets');
    expect(tickets?.href).toBeNull();
  });

  it('muestra la pestaña cuando el usuario efectivo tiene TICKET_VER_PROPIO', () => {
    mockHasAccion = (accion) => accion === 'TICKET_VER_PROPIO';
    render(<TabLayout />);

    const tickets = mockCapturedScreens.find((s) => s.name === 'tickets');
    expect(tickets?.href).toBeUndefined();
  });

  it('sin sesión (hasAccion siempre false) la pestaña queda oculta', () => {
    mockHasAccion = () => false;
    render(<TabLayout />);

    const tickets = mockCapturedScreens.find((s) => s.name === 'tickets');
    expect(tickets?.href).toBeNull();
  });

  it('regresión: una cuenta con únicamente TICKET_VALIDAR (Control) nunca ve "Mis entradas"', () => {
    mockHasAccion = (accion) => accion === 'TICKET_VALIDAR';
    render(<TabLayout />);

    const tickets = mockCapturedScreens.find((s) => s.name === 'tickets');
    expect(tickets?.href).toBeNull();
  });

  it('una cuenta multirol con TICKET_VER_PROPIO y TICKET_VALIDAR sí ve "Mis entradas": el gate nunca oculta una acción legítima', () => {
    mockHasAccion = (accion) => accion === 'TICKET_VER_PROPIO' || accion === 'TICKET_VALIDAR';
    render(<TabLayout />);

    const tickets = mockCapturedScreens.find((s) => s.name === 'tickets');
    expect(tickets?.href).toBeUndefined();
  });
});
