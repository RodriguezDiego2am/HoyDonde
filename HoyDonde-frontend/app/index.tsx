import { useEffect } from 'react';
import { router } from 'expo-router';

/** La cartelera pública vive en (tabs)/index.tsx y no requiere sesión. */
export default function Index() {
  useEffect(() => {
    router.replace('/(tabs)');
  }, []);

  return null;
}
