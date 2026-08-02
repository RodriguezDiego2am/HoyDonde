import { Platform } from 'react-native';

const ANDROID_EMULATOR_FALLBACK = 'http://10.0.2.2:5053/api';
const LOCAL_WEB_FALLBACK = 'http://localhost:5053/api';

/**
 * Prioriza EXPO_PUBLIC_API_URL. En desarrollo sin esa variable cae a un
 * localhost razonable según la plataforma. Nunca inventa una URL de
 * producción: fuera de __DEV__ sin la variable configurada, falla explícito.
 */
export function resolveApiUrl(): string {
  const configured = process.env.EXPO_PUBLIC_API_URL;
  if (configured) {
    return configured;
  }

  if (__DEV__) {
    return Platform.OS === 'android' ? ANDROID_EMULATOR_FALLBACK : LOCAL_WEB_FALLBACK;
  }

  throw new Error(
    'EXPO_PUBLIC_API_URL no está configurada. Definila en .env (o en la configuración de build) antes de compilar para producción.'
  );
}
