export interface FirebaseWebConfig {
  apiKey: string;
  authDomain: string;
  projectId: string;
  storageBucket: string;
  messagingSenderId: string;
  appId: string;
}

/**
 * Cada variable se lee con acceso estático (process.env.EXPO_PUBLIC_*) porque el
 * plugin de Babel de Expo solo inlinea expresiones estáticas de ese formato; una
 * búsqueda dinámica (process.env[key]) queda undefined en el bundle de producción.
 */
export function readFirebaseConfig(): FirebaseWebConfig {
  const config: FirebaseWebConfig = {
    apiKey: process.env.EXPO_PUBLIC_FIREBASE_API_KEY ?? '',
    authDomain: process.env.EXPO_PUBLIC_FIREBASE_AUTH_DOMAIN ?? '',
    projectId: process.env.EXPO_PUBLIC_FIREBASE_PROJECT_ID ?? '',
    storageBucket: process.env.EXPO_PUBLIC_FIREBASE_STORAGE_BUCKET ?? '',
    messagingSenderId: process.env.EXPO_PUBLIC_FIREBASE_MESSAGING_SENDER_ID ?? '',
    appId: process.env.EXPO_PUBLIC_FIREBASE_APP_ID ?? '',
  };

  const missing = (Object.keys(config) as (keyof FirebaseWebConfig)[]).filter((key) => !config[key]);

  if (missing.length > 0) {
    throw new Error(
      `Configuración de Firebase incompleta. Faltan variables de entorno EXPO_PUBLIC_FIREBASE_*: ${missing.join(
        ', '
      )}. Copiá HoyDonde-frontend/.env.example a .env y completá los valores públicos de tu proyecto Firebase (nunca la cuenta de servicio del backend).`
    );
  }

  return config;
}
