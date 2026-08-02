import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
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

  const runSync = useCallback(async (firebaseUser: FirebaseUser, payload?: SyncUserPayload) => {
    pendingFirebaseUser.current = firebaseUser;
    try {
      const result = await authApi.sync(payload);
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

  return (
    <AuthContext.Provider
      value={{ user, initializing, syncError, loginWithEmail, registerCliente, retrySync, logout, hasAccion }}
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
