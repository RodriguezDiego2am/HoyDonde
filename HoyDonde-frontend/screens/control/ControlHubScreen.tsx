import React, { useState } from 'react';
import { SafeAreaView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { controlDisplayName } from '@/utils/controlDisplayName';

/**
 * Hub exclusivo de Control (Frontend 3): reemplaza la lista temporal de eventos asignados como
 * pantalla principal. Para una cuenta Control exclusiva (ControlSessionGate, utils/controlExperience.ts)
 * es la única superficie de navegación disponible; para un multirol, se llega acá desde el Panel
 * (screens/(tabs)/explore.tsx) y el resto de su navegación sigue intacta.
 */
export default function ControlHubScreen() {
  const { user, logout } = useAuth();
  const [confirmLogoutVisible, setConfirmLogoutVisible] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);

  const displayName = controlDisplayName(user?.email);

  const handleLogout = async () => {
    setLoggingOut(true);
    await logout();
    setLoggingOut(false);
    setConfirmLogoutVisible(false);
    router.replace('/(tabs)');
  };

  return (
    <SafeAreaView style={styles.safe}>
      <View style={styles.header}>
        <View style={styles.badge}>
          <MaterialIcons name="verified-user" size={16} color={colors.paper} />
          <Text style={styles.badgeLabel}>{displayName}</Text>
        </View>
        <Text style={styles.title}>Control de acceso</Text>
      </View>

      <View style={styles.actions}>
        <ActionButton
          label="Escanear entrada"
          onPress={() => router.push('/control/scan')}
          accessibilityHint="Abre la cámara para escanear el código QR de una entrada"
        />
        <View style={styles.secondaryAction}>
          <ActionButton
            label="Ingresar código manualmente"
            variant="secondary"
            onPress={() => router.push('/control/manual')}
          />
        </View>
      </View>

      <View style={styles.secondaryLinksRow}>
        <ActionButton label="Cambiar contraseña" variant="ghost" onPress={() => router.push('/account/security')} />
      </View>

      <View style={styles.logoutRow}>
        <ActionButton label="Cerrar sesión" variant="ghost" onPress={() => setConfirmLogoutVisible(true)} />
      </View>

      <ConfirmDialog
        visible={confirmLogoutVisible}
        title="Cerrar sesión"
        message="Vas a salir de Control de acceso. Vas a necesitar volver a iniciar sesión para validar entradas."
        confirmLabel="Sí, cerrar sesión"
        variant="danger"
        loading={loggingOut}
        onConfirm={handleLogout}
        onCancel={() => setConfirmLogoutVisible(false)}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  header: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.xl,
    paddingBottom: spacing.xl,
    borderBottomWidth: 3,
    borderBottomColor: colors.ink,
  },
  badge: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: 6,
    backgroundColor: colors.ink,
    borderRadius: 14,
    paddingVertical: 4,
    paddingHorizontal: 10,
  },
  badgeLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    color: colors.paper,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 32,
    color: colors.ink,
    marginTop: spacing.md,
  },
  actions: {
    flex: 1,
    justifyContent: 'center',
    paddingHorizontal: spacing.lg,
  },
  secondaryAction: {
    marginTop: spacing.md,
  },
  secondaryLinksRow: {
    alignItems: 'center',
  },
  logoutRow: {
    alignItems: 'center',
    paddingBottom: spacing.xl,
  },
});
