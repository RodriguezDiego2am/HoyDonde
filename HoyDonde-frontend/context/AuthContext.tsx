import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { AppState, AppStateStatus } from 'react-native';
import {
  User as FirebaseUser,
  createUserWithEmailAndPassword,
  onAuthStateChanged,
  signInWithEmailAndPassword,
  signOut as firebaseSignOut,
} from 'firebase/auth';

import { auth } from '../config/firebase';
import { ApiError } from '../services/apiError';
import { SyncUserPayload, authApi } from '../services/APIService';

// Evita "spam" de refresh al volver la app al primer plano: si ya se refrescó (por cualquier
// vía, manual o de foreground) hace menos de este umbral, un nuevo regreso a foreground no
// dispara otra request.
const FOREGROUND_REFRESH_MIN_INTERVAL_MS = 60_000;

export interface AppUser {
  uid: string;
  email: string | null;
  usuarioId: string;
  personaId: string;
  roles: string[];
  acciones: string[];
}

export interface RegisterClienteInput {
  email: string;
  password: string;
  fullName: string;
  dni: string;
  phoneNumber: string;
}

interface AuthContextValue {
  user: AppUser | null;
  initializing: boolean;
  /** Mensaje de la última falla de sincronización pendiente, si la hay. */
  syncError: string | null;
  loginWithEmail: (email: string, password: string) => Promise<void>;
  registerCliente: (data: RegisterClienteInput) => Promise<void>;
  retrySync: () => Promise<void>;
  logout: () => Promise<void>;
  hasAccion: (accion: string) => boolean;
  /**
   * Reconsulta roles/acciones efectivas de la sesión actual (POST /api/auth/sync) sin cerrar
   * sesión ni reaprovisionar identidad. Ante un error, conserva la sesión vigente y relanza el
   * error para que quien llamó decida cómo mostrarlo (nunca reemplaza una sesión válida por
   * datos parciales).
   */
  refreshSessionPermissions: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AppUser | null>(null);
  const [initializing, setInitializing] = useState(true);
  const [syncError, setSyncError] = useState<string | null>(null);

  // Evita que el listener de onAuthStateChanged dispare un sync vacío mientras
  // registerCliente ya está sincronizando con los datos completos de Persona.
  const registrationInFlight = useRef(false);
  const pendingFirebaseUser = useRef<FirebaseUser | null>(null);

  // UID de la sesión más reciente que empezó a sincronizar. Si el usuario cierra sesión y
  // entra con otra cuenta antes de que la sincronización anterior termine (p. ej. probando
  // varias cuentas seguidas), las dos llamadas a /api/auth/sync quedan en vuelo a la vez y
  // pueden resolver en cualquier orden. Sin este chequeo, una respuesta tardía de la cuenta
  // vieja pisaba el estado ya vigente de la cuenta nueva con sus propias acciones (bug real:
  // una cuenta Control terminaba mostrando "Mis entradas" porque las acciones de un Cliente
  // logueado justo antes llegaban después y sobreescribían las del Control).
  const activeUidRef = useRef<string | null>(null);

  // Último FirebaseUser conocido, independiente de si su sync más reciente resolvió con éxito.
  // refreshSessionPermissions lo usa en vez de auth.currentUser para no depender del SDK real de
  // Firebase (facilita el testeo y evita una fuente de verdad paralela).
  const currentFirebaseUserRef = useRef<FirebaseUser | null>(null);

  // Evita refrescos concurrentes duplicados: una segunda llamada mientras la primera está en
  // vuelo reutiliza la misma promesa en vez de disparar otra request.
  const refreshInFlightRef = useRef<Promise<void> | null>(null);
  const lastRefreshAtRef = useRef<number>(0);

  const runSync = useCallback(async (firebaseUser: FirebaseUser, payload?: SyncUserPayload) => {
    const requestUid = firebaseUser.uid;
    currentFirebaseUserRef.current = firebaseUser;
    activeUidRef.current = requestUid;
    pendingFirebaseUser.current = firebaseUser;
    try {
      const result = await authApi.sync(payload);
      if (activeUidRef.current !== requestUid) return;
      setUser({
        uid: firebaseUser.uid,
        email: firebaseUser.email,
        usuarioId: result.usuarioId,
        personaId: result.personaId,
        roles: result.roles,
        acciones: result.acciones,
      });
      setSyncError(null);
      pendingFirebaseUser.current = null;
    } catch (error) {
      if (activeUidRef.current !== requestUid) return;
      setUser(null);
      setSyncError(
        error instanceof ApiError ? error.message : 'No se pudo sincronizar la sesión. Probá de nuevo.'
      );
    }
  }, []);

  useEffect(() => {
    const unsubscribe = onAuthStateChanged(auth, async (firebaseUser) => {
      // La restauración inicial de sesión de Firebase ya se resolvió, sea cual
      // sea el resultado; la sincronización con el backend puede seguir en curso.
      setInitializing(false);

      if (!firebaseUser) {
        activeUidRef.current = null;
        currentFirebaseUserRef.current = null;
        setUser(null);
        setSyncError(null);
        pendingFirebaseUser.current = null;
        return;
      }

      if (registrationInFlight.current) {
        return;
      }

      await runSync(firebaseUser);
    });

    return unsubscribe;
  }, [runSync]);

  const loginWithEmail = useCallback(async (email: string, password: string) => {
    await signInWithEmailAndPassword(auth, email, password);
    // onAuthStateChanged dispara el sync (también para Admin/Organizador/Control).
  }, []);

  const registerCliente = useCallback(
    async (data: RegisterClienteInput) => {
      registrationInFlight.current = true;
      try {
        const credential = await createUserWithEmailAndPassword(auth, data.email, data.password);
        await runSync(credential.user, {
          fullName: data.fullName,
          dni: data.dni,
          phoneNumber: data.phoneNumber,
        });
      } finally {
        registrationInFlight.current = false;
      }
    },
    [runSync]
  );

  const retrySync = useCallback(async () => {
    const firebaseUser = pendingFirebaseUser.current ?? auth.currentUser;
    if (!firebaseUser) {
      return;
    }
    await runSync(firebaseUser);
  }, [runSync]);

  const logout = useCallback(async () => {
    await firebaseSignOut(auth);
  }, []);

  const hasAccion = useCallback((accion: string) => user?.acciones.includes(accion) ?? false, [user]);

  // A diferencia de runSync/retrySync, un refresh fallido NUNCA reemplaza una sesión válida por
  // null ni por datos parciales: el usuario sigue viendo sus últimos roles/acciones conocidos y
  // el error se relanza para que la pantalla que llamó decida cómo mostrarlo (toast, banner, etc).
  const refreshSessionPermissions = useCallback(async (): Promise<void> => {
    if (refreshInFlightRef.current) {
      return refreshInFlightRef.current;
    }

    const firebaseUser = currentFirebaseUserRef.current;
    if (!firebaseUser) {
      return;
    }

    const requestUid = firebaseUser.uid;

    const task = (async () => {
      try {
        const result = await authApi.sync();
        if (activeUidRef.current !== requestUid) return;
        setUser((prev) =>
          prev && prev.uid === requestUid
            ? { ...prev, roles: result.roles, acciones: result.acciones }
            : prev
        );
      } catch (error) {
        if (activeUidRef.current !== requestUid) return;
        throw error instanceof ApiError
          ? error
          : new ApiError(
              { code: 'NETWORK_ERROR', message: 'No se pudieron actualizar los permisos. Probá de nuevo.', traceId: '' }
            );
      }
    })();

    lastRefreshAtRef.current = Date.now();
    refreshInFlightRef.current = task;
    try {
      await task;
    } finally {
      if (refreshInFlightRef.current === task) {
        refreshInFlightRef.current = null;
      }
    }
  }, []);

  // Refresco automático al volver la app al primer plano, acotado por
  // FOREGROUND_REFRESH_MIN_INTERVAL_MS para no generar una request cada vez que se cambia de
  // app brevemente. Los errores se descartan silenciosamente: es un refresco ambiental, no una
  // acción que el usuario esté esperando ver confirmada.
  useEffect(() => {
    const handleAppStateChange = (nextState: AppStateStatus) => {
      if (nextState !== 'active') return;
      if (!currentFirebaseUserRef.current) return;
      if (Date.now() - lastRefreshAtRef.current < FOREGROUND_REFRESH_MIN_INTERVAL_MS) return;

      refreshSessionPermissions().catch(() => {});
    };

    const subscription = AppState.addEventListener('change', handleAppStateChange);
    return () => subscription.remove();
  }, [refreshSessionPermissions]);

  return (
    <AuthContext.Provider
      value={{
        user,
        initializing,
        syncError,
        loginWithEmail,
        registerCliente,
        retrySync,
        logout,
        hasAccion,
        refreshSessionPermissions,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth debe usarse dentro de un AuthProvider');
  }
  return context;
}
