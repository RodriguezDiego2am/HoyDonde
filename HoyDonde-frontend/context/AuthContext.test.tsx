/* eslint-disable import/first -- los jest.mock deben preceder al import de AuthContext que los usa */
import React from 'react';
import { AppState, Pressable, Text } from 'react-native';
import { act, fireEvent, render, waitFor } from '@testing-library/react-native';

let mockAuthStateCallback: ((user: unknown) => void) | null = null;
const mockUnsubscribe = jest.fn();
const mockOnAuthStateChanged = jest.fn((_auth: unknown, callback: (user: unknown) => void) => {
  mockAuthStateCallback = callback;
  return mockUnsubscribe;
});
const mockSignInWithEmailAndPassword = jest.fn();
const mockCreateUserWithEmailAndPassword = jest.fn();
const mockSignOut = jest.fn().mockResolvedValue(undefined);

jest.mock('firebase/auth', () => ({
  onAuthStateChanged: (...args: unknown[]) => (mockOnAuthStateChanged as any)(...args),
  signInWithEmailAndPassword: (...args: unknown[]) => (mockSignInWithEmailAndPassword as any)(...args),
  createUserWithEmailAndPassword: (...args: unknown[]) => (mockCreateUserWithEmailAndPassword as any)(...args),
  signOut: (...args: unknown[]) => (mockSignOut as any)(...args),
}));

jest.mock('../config/firebase', () => ({ auth: {} }));

const mockSync = jest.fn();
jest.mock('../services/APIService', () => ({ authApi: { sync: (...args: unknown[]) => mockSync(...args) } }));

import { AuthProvider, useAuth } from './AuthContext';
import type { RegisterClienteInput } from './AuthContext';

function AuthProbe() {
  const {
    user,
    initializing,
    syncError,
    loginWithEmail,
    registerCliente,
    retrySync,
    logout,
    hasAccion,
    refreshSessionPermissions,
  } = useAuth();
  const register = (data: RegisterClienteInput) => () => registerCliente(data).catch(() => {});

  return (
    <>
      <Text testID="initializing">{String(initializing)}</Text>
      <Text testID="user">{user ? JSON.stringify(user) : 'null'}</Text>
      <Text testID="syncError">{syncError ?? 'null'}</Text>
      <Text testID="hasAccion">{String(hasAccion('TICKET_COMPRAR'))}</Text>
      <Pressable testID="login" onPress={() => loginWithEmail('cliente@hoydonde.com', 'secret1').catch(() => {})} />
      <Pressable
        testID="register"
        onPress={register({ email: 'nueva@hoydonde.com', password: 'secret1', fullName: 'Ada', dni: '123', phoneNumber: '+54' })}
      />
      <Pressable testID="retry" onPress={() => retrySync()} />
      <Pressable testID="logout" onPress={() => logout()} />
      <Pressable testID="refreshPermissions" onPress={() => refreshSessionPermissions().catch(() => {})} />
    </>
  );
}

function renderAuth() {
  return render(
    <AuthProvider>
      <AuthProbe />
    </AuthProvider>
  );
}

const fakeUser = (uid: string, email: string) => ({ uid, email });

describe('AuthContext', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockAuthStateCallback = null;
  });

  it('starts initializing and resolves once Firebase reports no session', async () => {
    const { getByTestId } = renderAuth();
    expect(getByTestId('initializing').props.children).toBe('true');

    await act(async () => {
      mockAuthStateCallback?.(null);
    });

    expect(getByTestId('initializing').props.children).toBe('false');
    expect(getByTestId('user').props.children).toBe('null');
  });

  it('syncs with the backend after Firebase reports a session (login), including a privileged user', async () => {
    mockSync.mockResolvedValueOnce({
      usuarioId: 'usuario-1',
      personaId: 'persona-1',
      roles: ['ORGANIZADOR'],
      acciones: ['EVENTO_CREAR'],
    });

    const { getByTestId } = renderAuth();

    await act(async () => {
      mockAuthStateCallback?.(fakeUser('uid-1', 'organizador@hoydonde.com'));
    });

    expect(mockSync).toHaveBeenCalledWith(undefined);
    expect(JSON.parse(getByTestId('user').props.children)).toMatchObject({
      uid: 'uid-1',
      roles: ['ORGANIZADOR'],
      acciones: ['EVENTO_CREAR'],
    });
    // hasAccion consulta 'TICKET_COMPRAR', que este Organizador no tiene.
    expect(getByTestId('hasAccion').props.children).toBe('false');
  });

  it('hasAccion reflects the acciones returned by the last sync', async () => {
    mockSync.mockResolvedValueOnce({
      usuarioId: 'usuario-4',
      personaId: 'persona-4',
      roles: ['CLIENTE'],
      acciones: ['TICKET_COMPRAR', 'TICKET_VER_PROPIO'],
    });

    const { getByTestId } = renderAuth();
    await act(async () => {
      mockAuthStateCallback?.(fakeUser('uid-4', 'cliente2@hoydonde.com'));
    });

    expect(getByTestId('hasAccion').props.children).toBe('true');
  });

  it('registerCliente sends the Persona payload exactly once and the listener does not run an empty sync meanwhile', async () => {
    let resolveCreate!: (value: { user: unknown }) => void;
    mockCreateUserWithEmailAndPassword.mockReturnValue(
      new Promise((resolve) => {
        resolveCreate = resolve;
      })
    );
    mockSync.mockResolvedValue({
      usuarioId: 'usuario-2',
      personaId: 'persona-2',
      roles: ['CLIENTE'],
      acciones: ['TICKET_COMPRAR'],
    });

    const { getByTestId } = renderAuth();
    await act(async () => {
      mockAuthStateCallback?.(null);
    });

    fireEvent.press(getByTestId('register'));

    // Firebase podría notificar el nuevo usuario por su propio listener mientras
    // createUserWithEmailAndPassword todavía no resolvió: no debe disparar un sync vacío.
    await act(async () => {
      mockAuthStateCallback?.(fakeUser('uid-2', 'nueva@hoydonde.com'));
    });
    expect(mockSync).not.toHaveBeenCalled();

    await act(async () => {
      resolveCreate({ user: fakeUser('uid-2', 'nueva@hoydonde.com') });
    });

    await waitFor(() => expect(mockSync).toHaveBeenCalledTimes(1));
    expect(mockSync).toHaveBeenCalledWith({ fullName: 'Ada', dni: '123', phoneNumber: '+54' });
  });

  it('keeps the Firebase identity and allows retrying after a failed sync, without recreating it', async () => {
    mockSync.mockRejectedValueOnce(new Error('network down'));

    const { getByTestId } = renderAuth();
    await act(async () => {
      mockAuthStateCallback?.(fakeUser('uid-3', 'cliente@hoydonde.com'));
    });

    expect(getByTestId('user').props.children).toBe('null');
    expect(getByTestId('syncError').props.children).not.toBe('null');

    mockSync.mockResolvedValueOnce({
      usuarioId: 'usuario-3',
      personaId: 'persona-3',
      roles: ['CLIENTE'],
      acciones: [],
    });

    fireEvent.press(getByTestId('retry'));

    await waitFor(() => expect(JSON.parse(getByTestId('user').props.children)).toMatchObject({ uid: 'uid-3' }));
    expect(mockCreateUserWithEmailAndPassword).not.toHaveBeenCalled();
    expect(getByTestId('syncError').props.children).toBe('null');
  });

  it('discards a stale sync response from a superseded account (fast account switch race)', async () => {
    let resolveFirstSync!: (value: unknown) => void;
    mockSync.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveFirstSync = resolve;
      })
    );

    const { getByTestId } = renderAuth();

    // Cuenta A (Cliente) empieza a sincronizar pero la respuesta todavía no llega.
    await act(async () => {
      mockAuthStateCallback?.(fakeUser('uid-cliente', 'cliente@hoydonde.com'));
    });
    expect(getByTestId('user').props.children).toBe('null');

    // Antes de que A resuelva, Firebase ya reporta la cuenta B (Control) — dispara un segundo
    // sync que sí resuelve rápido.
    mockSync.mockResolvedValueOnce({
      usuarioId: 'usuario-control',
      personaId: 'persona-control',
      roles: ['CONTROL'],
      acciones: ['TICKET_VALIDAR'],
    });
    await act(async () => {
      mockAuthStateCallback?.(fakeUser('uid-control', 'control@hoydonde.com'));
    });

    expect(JSON.parse(getByTestId('user').props.children)).toMatchObject({
      uid: 'uid-control',
      acciones: ['TICKET_VALIDAR'],
    });

    // La respuesta tardía de la cuenta A (con acciones de Cliente) finalmente llega: no debe
    // pisar el estado ya vigente de la cuenta B.
    await act(async () => {
      resolveFirstSync({
        usuarioId: 'usuario-cliente',
        personaId: 'persona-cliente',
        roles: ['CLIENTE'],
        acciones: ['TICKET_COMPRAR', 'TICKET_VER_PROPIO'],
      });
    });

    expect(JSON.parse(getByTestId('user').props.children)).toMatchObject({
      uid: 'uid-control',
      acciones: ['TICKET_VALIDAR'],
    });
  });

  it('logout calls Firebase signOut', async () => {
    const { getByTestId } = renderAuth();
    await act(async () => {
      mockAuthStateCallback?.(null);
    });

    fireEvent.press(getByTestId('logout'));

    await waitFor(() => expect(mockSignOut).toHaveBeenCalledTimes(1));
  });

  function getAppStateChangeHandler(): (state: string) => void {
    const calls = [...(AppState.addEventListener as jest.Mock).mock.calls].reverse();
    const changeCall = calls.find((call) => call[0] === 'change');
    if (!changeCall) throw new Error('AppState "change" listener was not registered');
    return changeCall[1];
  }

  describe('refreshSessionPermissions', () => {
    it('reconsulta /api/auth/sync y actualiza roles/acciones sin tocar uid/usuarioId', async () => {
      mockSync.mockResolvedValueOnce({
        usuarioId: 'usuario-organizador',
        personaId: 'persona-organizador',
        roles: ['ORGANIZADOR'],
        acciones: ['EVENTO_CREAR'],
      });
      const { getByTestId } = renderAuth();
      await act(async () => {
        mockAuthStateCallback?.(fakeUser('uid-org', 'organizador@hoydonde.com'));
      });

      mockSync.mockResolvedValueOnce({
        usuarioId: 'usuario-organizador',
        personaId: 'persona-organizador',
        roles: ['ORGANIZADOR'],
        acciones: [],
      });
      fireEvent.press(getByTestId('refreshPermissions'));

      await waitFor(() =>
        expect(JSON.parse(getByTestId('user').props.children)).toMatchObject({ uid: 'uid-org', acciones: [] })
      );
      expect(mockSync).toHaveBeenCalledTimes(2);
    });

    it('dos llamadas concurrentes reutilizan la misma solicitud en vuelo', async () => {
      mockSync.mockResolvedValueOnce({
        usuarioId: 'usuario-organizador',
        personaId: 'persona-organizador',
        roles: ['ORGANIZADOR'],
        acciones: ['EVENTO_CREAR'],
      });
      const { getByTestId } = renderAuth();
      await act(async () => {
        mockAuthStateCallback?.(fakeUser('uid-org', 'organizador@hoydonde.com'));
      });

      let resolveRefresh!: (value: unknown) => void;
      mockSync.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveRefresh = resolve;
        })
      );

      fireEvent.press(getByTestId('refreshPermissions'));
      fireEvent.press(getByTestId('refreshPermissions'));
      fireEvent.press(getByTestId('refreshPermissions'));

      // 1 sync del login + 1 solo del refresh, aunque se haya presionado tres veces.
      expect(mockSync).toHaveBeenCalledTimes(2);

      await act(async () => {
        resolveRefresh({
          usuarioId: 'usuario-organizador',
          personaId: 'persona-organizador',
          roles: ['ORGANIZADOR'],
          acciones: ['EVENTO_CREAR'],
        });
      });
      expect(mockSync).toHaveBeenCalledTimes(2);
    });

    it('un refresh fallido conserva la sesión vigente en vez de reemplazarla', async () => {
      mockSync.mockResolvedValueOnce({
        usuarioId: 'usuario-organizador',
        personaId: 'persona-organizador',
        roles: ['ORGANIZADOR'],
        acciones: ['EVENTO_CREAR'],
      });
      const { getByTestId } = renderAuth();
      await act(async () => {
        mockAuthStateCallback?.(fakeUser('uid-org', 'organizador@hoydonde.com'));
      });

      mockSync.mockRejectedValueOnce(new Error('network down'));
      fireEvent.press(getByTestId('refreshPermissions'));

      await waitFor(() => expect(mockSync).toHaveBeenCalledTimes(2));
      // La sesión sigue intacta: ni se borra el usuario ni se pisan sus acciones por un error.
      expect(JSON.parse(getByTestId('user').props.children)).toMatchObject({
        uid: 'uid-org',
        acciones: ['EVENTO_CREAR'],
      });
    });

    it('vuelve a sincronizar al volver la app al primer plano', async () => {
      mockSync.mockResolvedValueOnce({
        usuarioId: 'usuario-organizador',
        personaId: 'persona-organizador',
        roles: ['ORGANIZADOR'],
        acciones: ['EVENTO_CREAR'],
      });
      renderAuth();
      await act(async () => {
        mockAuthStateCallback?.(fakeUser('uid-org', 'organizador@hoydonde.com'));
      });

      mockSync.mockResolvedValue({
        usuarioId: 'usuario-organizador',
        personaId: 'persona-organizador',
        roles: ['ORGANIZADOR'],
        acciones: ['EVENTO_CREAR'],
      });
      const handleAppStateChange = getAppStateChangeHandler();

      await act(async () => {
        handleAppStateChange('active');
      });
      await waitFor(() => expect(mockSync).toHaveBeenCalledTimes(2));

      // Un segundo regreso a foreground inmediatamente después no dispara otra request (spam).
      await act(async () => {
        handleAppStateChange('active');
      });
      expect(mockSync).toHaveBeenCalledTimes(2);
    });
  });
});
