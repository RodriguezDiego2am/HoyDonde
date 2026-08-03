import { useEffect } from 'react';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import {
  Archivo_400Regular,
  Archivo_500Medium,
  Archivo_600SemiBold,
  Archivo_700Bold,
  Archivo_900Black,
  useFonts,
} from '@expo-google-fonts/archivo';

import { ControlSessionGate } from '../components/control/ControlSessionGate';
import { AuthProvider } from '../context/AuthContext';

SplashScreen.preventAutoHideAsync().catch(() => {});

export default function RootLayout() {
  const [fontsLoaded, fontError] = useFonts({
    Archivo_400Regular,
    Archivo_500Medium,
    Archivo_600SemiBold,
    Archivo_700Bold,
    Archivo_900Black,
  });

  useEffect(() => {
    if (fontsLoaded || fontError) {
      SplashScreen.hideAsync().catch(() => {});
    }
  }, [fontsLoaded, fontError]);

  if (!fontsLoaded && !fontError) {
    return null;
  }

  return (
    <AuthProvider>
      <ControlSessionGate />
      <Stack screenOptions={{ headerShown: false }}>
        <Stack.Screen name="(tabs)" />
        <Stack.Screen name="login" />
        <Stack.Screen name="register" />
        <Stack.Screen name="events/[id]" />
        <Stack.Screen name="admin/index" />
        <Stack.Screen name="admin/altas" />
        <Stack.Screen name="admin/roles/index" />
        <Stack.Screen name="admin/roles/[codigo]/index" />
        <Stack.Screen name="admin/usuarios/index" />
        <Stack.Screen name="admin/usuarios/[usuarioId]/index" />
        <Stack.Screen name="organizer/index" />
        <Stack.Screen name="organizer/new" />
        <Stack.Screen name="organizer/[id]/index" />
        <Stack.Screen name="organizer/[id]/edit" />
        <Stack.Screen name="organizer/[id]/control-new" />
        <Stack.Screen name="organizer/[id]/control-assign" />
        <Stack.Screen name="control/index" />
        <Stack.Screen name="control/scan" />
        <Stack.Screen name="control/manual" />
      </Stack>
    </AuthProvider>
  );
}
