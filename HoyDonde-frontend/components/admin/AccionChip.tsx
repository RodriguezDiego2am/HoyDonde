import React from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

interface AccionChipProps {
  /** Nombre legible (AccionResponseDto.Descripcion), nunca solo el código. */
  nombre: string;
  codigo: string;
  /** Asignada a este Rol o no. Decide el estilo relleno/contorno y el ícono +/×. */
  assigned: boolean;
  /** true mientras la Accion está inactiva en el catálogo (informativo, no bloquea la asignación). */
  accionInactiva?: boolean;
  loading?: boolean;
  disabled?: boolean;
  onPress: () => void;
}

/** Chip de acción del catálogo: nombre legible primero, código técnico como dato secundario. Asignar no pide confirmación; quitar sí (la decide la pantalla que la usa). */
export function AccionChip({ nombre, codigo, assigned, accionInactiva, loading, disabled, onPress }: AccionChipProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={assigned ? `Quitar ${nombre}` : `Asignar ${nombre}`}
      accessibilityState={{ disabled: disabled || loading }}
      onPress={onPress}
      disabled={disabled || loading}
      style={({ pressed }) => [
        styles.chip,
        assigned ? styles.chipAssigned : styles.chipUnassigned,
        pressed && !disabled && !loading && styles.chipPressed,
        (disabled || loading) && styles.chipDisabled,
      ]}
    >
      <View style={styles.textBlock}>
        <Text style={[styles.nombre, assigned && styles.nombreAssigned]} numberOfLines={1}>
          {nombre}
        </Text>
        <Text style={[styles.codigo, assigned && styles.codigoAssigned]}>
          {codigo}
          {accionInactiva ? ' · inactiva' : ''}
        </Text>
      </View>
      {loading ? (
        <ActivityIndicator size="small" color={assigned ? colors.paper : colors.ink} />
      ) : (
        <MaterialIcons name={assigned ? 'close' : 'add'} size={18} color={assigned ? colors.paper : colors.ink} />
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    marginBottom: spacing.sm,
  },
  chipAssigned: {
    backgroundColor: colors.ink,
  },
  chipUnassigned: {
    backgroundColor: colors.paper,
  },
  chipPressed: {
    opacity: 0.75,
  },
  chipDisabled: {
    opacity: 0.5,
  },
  textBlock: {
    flexShrink: 1,
  },
  nombre: {
    fontFamily: fonts.semiBold,
    fontSize: 14,
    color: colors.ink,
  },
  nombreAssigned: {
    color: colors.paper,
  },
  codigo: {
    fontFamily: fonts.medium,
    fontSize: 11,
    letterSpacing: 0.4,
    color: colors.inkSoft,
    marginTop: 2,
  },
  codigoAssigned: {
    color: colors.sand,
  },
});
