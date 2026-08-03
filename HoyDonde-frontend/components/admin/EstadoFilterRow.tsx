import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';

export type EstadoFiltro = 'todos' | 'activos' | 'inactivos';

const OPCIONES: { value: EstadoFiltro; label: string }[] = [
  { value: 'todos', label: 'Todos' },
  { value: 'activos', label: 'Activos' },
  { value: 'inactivos', label: 'Inactivos' },
];

interface EstadoFilterRowProps {
  value: EstadoFiltro;
  onChange: (value: EstadoFiltro) => void;
}

/** Filtro activo/inactivo compartido por Roles y Usuarios: siempre en memoria sobre datos ya cargados. */
export function EstadoFilterRow({ value, onChange }: EstadoFilterRowProps) {
  return (
    <View style={styles.row}>
      {OPCIONES.map((opcion) => {
        const active = value === opcion.value;
        return (
          <Pressable
            key={opcion.value}
            accessibilityRole="button"
            accessibilityState={{ selected: active }}
            accessibilityLabel={`Filtrar por ${opcion.label}`}
            onPress={() => onChange(opcion.value)}
            style={[styles.chip, active && styles.chipActive]}
            hitSlop={4}
          >
            <Text style={[styles.chipText, active && styles.chipTextActive]} numberOfLines={1}>
              {opcion.label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  chip: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 10,
    paddingHorizontal: 8,
    minHeight: 40,
  },
  chipActive: {
    backgroundColor: colors.ink,
  },
  chipText: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.ink,
    textTransform: 'uppercase',
    textAlign: 'center',
  },
  chipTextActive: {
    color: colors.paper,
  },
});
