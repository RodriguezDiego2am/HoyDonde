import React from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';

import { ActionButton } from '@/components/ui/ActionButton';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';

export default function ProfileScreen() {
  const { user, initializing, syncError, retrySync, logout } = useAuth();

  if (initializing) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.tomato} />
      </View>
    );
  }

  const handleLogout = async () => {
    await logout();
    router.replace('/(tabs)');
  };

  if (user) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Perfil</Text>
        <Surface style={styles.card}>
          <Text style={styles.label}>Email</Text>
          <Text style={styles.value}>{user.email ?? 'Sin email'}</Text>

          <Text style={styles.label}>Roles</Text>
          <Text style={styles.value}>{user.roles.length > 0 ? user.roles.join(', ') : 'Sin roles asignados'}</Text>
        </Surface>
        <View style={styles.buttonContainer}>
          <ActionButton label="Cerrar sesión" variant="danger" onPress={handleLogout} />
        </View>
      </View>
    );
  }

  if (syncError) {
    return (
      <View style={styles.container}>
        <Text style={styles.title}>Perfil</Text>
        <Surface style={styles.card}>
          <Text style={styles.value}>No pudimos terminar de sincronizar tu sesión.</Text>
          <Text style={styles.errorText}>{syncError}</Text>
        </Surface>
        <View style={styles.buttonContainer}>
          <ActionButton label="Reintentar" onPress={retrySync} />
        </View>
        <View style={styles.buttonContainer}>
          <ActionButton label="Cerrar sesión" variant="secondary" onPress={handleLogout} />
        </View>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Perfil</Text>
      <Surface style={styles.card}>
        <Text style={styles.value}>Iniciá sesión para ver tu perfil, tus entradas y tus datos.</Text>
      </Surface>
      <View style={styles.buttonContainer}>
        <ActionButton
          label="Iniciar sesión"
          onPress={() => router.push({ pathname: '/login', params: { returnTo: '/(tabs)/explore' } })}
        />
      </View>
      <View style={styles.buttonContainer}>
        <ActionButton label="Crear cuenta" variant="secondary" onPress={() => router.push('/register')} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
    paddingTop: 64,
    paddingHorizontal: spacing.lg,
  },
  center: {
    flex: 1,
    backgroundColor: colors.paper,
    justifyContent: 'center',
    alignItems: 'center',
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 32,
    color: colors.ink,
    marginBottom: spacing.lg,
  },
  card: {
    marginBottom: spacing.md,
  },
  label: {
    fontFamily: fonts.semiBold,
    fontSize: 13,
    color: colors.ink,
    opacity: 0.6,
    marginTop: spacing.sm,
  },
  value: {
    fontFamily: fonts.regular,
    fontSize: 16,
    color: colors.ink,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.error,
    marginTop: spacing.sm,
  },
  buttonContainer: {
    marginTop: spacing.sm,
  },
});
