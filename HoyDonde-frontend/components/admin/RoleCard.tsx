import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { StatusStamp } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import { RolResponse } from '@/services/securityAdminService';

interface RoleCardProps {
  rol: RolResponse;
  onPress: () => void;
}

/** Ficha de archivo de un Rol: nombre legible primero, código como dato secundario de identificación. */
export function RoleCard({ rol, onPress }: RoleCardProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Ver rol ${rol.nombre}`}
      onPress={onPress}
      style={({ pressed }) => pressed && styles.pressed}
    >
      <Surface variant="sand" style={styles.card}>
        <View style={styles.header}>
          <Text style={styles.nombre}>{rol.nombre}</Text>
          <StatusStamp label={rol.activo ? 'Activo' : 'Inactivo'} tone={rol.activo ? 'success' : 'error'} />
        </View>
        <Text style={styles.codigo}>{rol.codigo}</Text>
        {rol.descripcion ? (
          <Text style={styles.descripcion} numberOfLines={2}>
            {rol.descripcion}
          </Text>
        ) : null}
        <Text style={styles.accionesCount}>
          {rol.acciones.length} {rol.acciones.length === 1 ? 'acción asignada' : 'acciones asignadas'}
        </Text>
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
  nombre: {
    fontFamily: fonts.bold,
    fontSize: 17,
    color: colors.ink,
    flexShrink: 1,
  },
  codigo: {
    fontFamily: fonts.medium,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.inkSoft,
    marginTop: 2,
  },
  descripcion: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.ink,
    marginTop: spacing.sm,
  },
  accionesCount: {
    fontFamily: fonts.semiBold,
    fontSize: 12,
    letterSpacing: 0.3,
    color: colors.inkSoft,
    marginTop: spacing.sm,
  },
});
