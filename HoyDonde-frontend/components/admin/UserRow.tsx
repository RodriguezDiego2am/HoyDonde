import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { StatusStamp } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import { UsuarioResumenResponse } from '@/services/securityAdminService';

interface UserRowProps {
  usuario: UsuarioResumenResponse;
  onPress: () => void;
}

/** Fila de archivo de un Usuario: email primero, nunca UsuarioId/PersonaId visibles. */
export function UserRow({ usuario, onPress }: UserRowProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Ver usuario ${usuario.email}`}
      onPress={onPress}
      style={({ pressed }) => pressed && styles.pressed}
    >
      <Surface variant="sand" style={styles.card}>
        <View style={styles.header}>
          <Text style={styles.email} numberOfLines={1}>
            {usuario.email}
          </Text>
          <StatusStamp label={usuario.activo ? 'Activo' : 'Inactivo'} tone={usuario.activo ? 'success' : 'error'} />
        </View>
        {usuario.rolesActivos.length > 0 ? (
          <View style={styles.badgeRow}>
            {usuario.rolesActivos.map((rol) => (
              <StatusStamp key={rol} label={rol} tone="cobalt" />
            ))}
          </View>
        ) : (
          <Text style={styles.sinRoles}>Sin roles activos</Text>
        )}
      </Surface>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  pressed: {
    opacity: 0.85,
  },
  card: {
    marginBottom: spacing.md,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  email: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
    flexShrink: 1,
  },
  badgeRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
    marginTop: spacing.sm,
  },
  sinRoles: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
    marginTop: spacing.sm,
  },
});
