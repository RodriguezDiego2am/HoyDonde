import React from 'react';
import { ActivityIndicator, SafeAreaView, ScrollView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';

import { ActionButton } from '@/components/ui/ActionButton';
import { StatusStamp } from '@/components/ui/StatusStamp';
import { TicketCard } from '@/components/ui/TicketCard';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';

const ACCIONES_PREVIEW_LIMIT = 6;

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
    const accionesPreview = user.acciones.slice(0, ACCIONES_PREVIEW_LIMIT);
    const accionesRestantes = user.acciones.length - accionesPreview.length;

    return (
      <SafeAreaView style={styles.safe}>
        <ScrollView contentContainerStyle={styles.scroll}>
          <View style={styles.header}>
            <Text style={styles.eyebrow}>TU CUENTA</Text>
            <Text style={styles.title}>Perfil</Text>
          </View>

          <TicketCard
            orientation="column"
            stub={
              <View style={styles.passStub}>
                <Text style={styles.wordmark}>HOYDONDE?</Text>
                <Text style={styles.passLabel}>PASE PERSONAL</Text>
              </View>
            }
          >
            <Text style={styles.fieldLabel}>Email</Text>
            <Text style={styles.fieldValue}>{user.email ?? 'Sin email'}</Text>

            <Text style={[styles.fieldLabel, styles.fieldSpacing]}>Roles</Text>
            {user.roles.length > 0 ? (
              <View style={styles.badgeRow}>
                {user.roles.map((rol) => (
                  <StatusStamp key={rol} label={rol} tone="cobalt" />
                ))}
              </View>
            ) : (
              <Text style={styles.fieldValue}>Sin roles asignados</Text>
            )}

            <View style={styles.innerHairline} />

            <View style={styles.accionesHeader}>
              <Text style={styles.accionesCount}>{user.acciones.length}</Text>
              <Text style={styles.accionesLabel}>ACCIONES HABILITADAS</Text>
            </View>
            {accionesPreview.length > 0 ? (
              <View style={styles.badgeRow}>
                {accionesPreview.map((accion) => (
                  <View key={accion} style={styles.accionChip}>
                    <Text style={styles.accionChipText}>{accion}</Text>
                  </View>
                ))}
                {accionesRestantes > 0 ? (
                  <View style={styles.accionChip}>
                    <Text style={styles.accionChipText}>+{accionesRestantes} más</Text>
                  </View>
                ) : null}
              </View>
            ) : null}
          </TicketCard>

          <View style={styles.logoutRow}>
            <ActionButton label="Cerrar sesión" variant="ghost" onPress={handleLogout} />
          </View>
        </ScrollView>
      </SafeAreaView>
    );
  }

  if (syncError) {
    return (
      <SafeAreaView style={styles.safe}>
        <ScrollView contentContainerStyle={styles.scroll}>
          <View style={styles.header}>
            <Text style={styles.eyebrow}>TU CUENTA</Text>
            <Text style={styles.title}>Perfil</Text>
          </View>

          <TicketCard
            orientation="column"
            stub={
              <View style={styles.passStub}>
                <Text style={styles.wordmark}>HOYDONDE?</Text>
                <Text style={styles.passLabel}>PASE SIN VALIDAR</Text>
              </View>
            }
          >
            <StatusStamp label="Sincronización pendiente" tone="error" />
            <Text style={[styles.fieldValue, styles.fieldSpacing]}>
              No pudimos terminar de sincronizar tu sesión.
            </Text>
            <Text style={styles.errorText}>{syncError}</Text>
          </TicketCard>

          <View style={styles.buttonContainer}>
            <ActionButton label="Reintentar" onPress={retrySync} />
          </View>
          <View style={styles.logoutRow}>
            <ActionButton label="Cerrar sesión" variant="ghost" onPress={handleLogout} />
          </View>
        </ScrollView>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.safe}>
      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.header}>
          <Text style={styles.eyebrow}>TU CUENTA</Text>
          <Text style={styles.title}>Perfil</Text>
        </View>

        <TicketCard
          orientation="column"
          stub={
            <View style={styles.passStub}>
              <Text style={styles.wordmark}>HOYDONDE?</Text>
              <Text style={styles.passLabel}>ENTRADA REQUERIDA</Text>
            </View>
          }
        >
          <Text style={styles.fieldValue}>Iniciá sesión para ver tu perfil, tus entradas y tus datos.</Text>
        </TicketCard>

        <View style={styles.buttonContainer}>
          <ActionButton
            label="Iniciar sesión"
            onPress={() => router.push({ pathname: '/login', params: { returnTo: '/(tabs)/explore' } })}
          />
        </View>
        <View style={styles.buttonContainer}>
          <ActionButton label="Crear cuenta" variant="secondary" onPress={() => router.push('/register')} />
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  scroll: {
    flexGrow: 1,
    padding: spacing.lg,
  },
  center: {
    flex: 1,
    backgroundColor: colors.paper,
    justifyContent: 'center',
    alignItems: 'center',
  },
  header: {
    marginBottom: spacing.lg,
  },
  eyebrow: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1.2,
    color: colors.inkSoft,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 32,
    color: colors.ink,
  },
  passStub: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  wordmark: {
    fontFamily: fonts.bold,
    fontSize: 13,
    letterSpacing: 1.5,
    color: colors.ink,
  },
  passLabel: {
    fontFamily: fonts.semiBold,
    fontSize: 11,
    letterSpacing: 1,
    color: colors.inkSoft,
  },
  fieldLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    color: colors.inkSoft,
  },
  fieldSpacing: {
    marginTop: spacing.md,
  },
  fieldValue: {
    fontFamily: fonts.regular,
    fontSize: 16,
    color: colors.ink,
    marginTop: 4,
  },
  badgeRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  innerHairline: {
    height: 1,
    backgroundColor: colors.ink,
    opacity: 0.2,
    marginTop: spacing.lg,
    marginBottom: spacing.md,
  },
  accionesHeader: {
    flexDirection: 'row',
    alignItems: 'baseline',
    gap: spacing.sm,
  },
  accionesCount: {
    fontFamily: fonts.black,
    fontSize: 26,
    color: colors.tomato,
  },
  accionesLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1,
    color: colors.inkSoft,
  },
  accionChip: {
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 3,
    paddingHorizontal: 8,
    backgroundColor: colors.paper,
  },
  accionChipText: {
    fontFamily: fonts.medium,
    fontSize: 11,
    letterSpacing: 0.3,
    color: colors.ink,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.error,
    marginTop: spacing.sm,
  },
  buttonContainer: {
    marginTop: spacing.lg,
  },
  logoutRow: {
    marginTop: spacing.xl,
    alignItems: 'center',
  },
});
