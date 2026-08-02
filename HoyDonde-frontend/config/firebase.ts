import { getApp, getApps, initializeApp } from 'firebase/app';
// getReactNativePersistence solo existe bajo la condición "react-native" del
// package.json de firebase; su entrada "types" apunta siempre al d.ts genérico
// y no la declara, así que TS no la ve pese a existir en el build RN real
// (limitación conocida del empaquetado de firebase/auth, no de este código).
// @ts-expect-error -- getReactNativePersistence no está en los tipos genéricos, ver comentario arriba
import { Auth, getAuth, getReactNativePersistence, initializeAuth } from 'firebase/auth';
import AsyncStorage from '@react-native-async-storage/async-storage';

import { readFirebaseConfig } from './firebaseEnv';

export const firebaseApp = getApps().length ? getApp() : initializeApp(readFirebaseConfig());

function createNativeAuth(): Auth {
  try {
    return initializeAuth(firebaseApp, { persistence: getReactNativePersistence(AsyncStorage) });
  } catch {
    // Fast Refresh ya inicializó auth para esta app: reutilizamos la instancia existente
    // en vez de fallar, porque initializeAuth() no puede llamarse dos veces por app.
    return getAuth(firebaseApp);
  }
}

export const auth = createNativeAuth();
