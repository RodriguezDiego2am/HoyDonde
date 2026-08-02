import { getApp, getApps, initializeApp } from 'firebase/app';
import { getAuth } from 'firebase/auth';

import { readFirebaseConfig } from './firebaseEnv';

export const firebaseApp = getApps().length ? getApp() : initializeApp(readFirebaseConfig());

// El SDK web de Firebase Auth persiste la sesión (indexedDB, con fallback a
// localStorage) por defecto, así que getAuth() alcanza sin configurar
// persistencia explícita.
export const auth = getAuth(firebaseApp);
