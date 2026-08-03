import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

export interface ChipOption<T extends string> {
  value: T;
  label: string;
}

interface ChipSelectRowProps<T extends string> {
  options: ChipOption<T>[];
  selected: T | undefined;
  onSelect: (value: T | undefined) => void;
  allLabel?: string;
}

/** Fila de chips de selección única con opción "Todos" (deselecciona), compartida por los filtros de estado/categoría/objetivo de los tres reportes. */
export function ChipSelectRow<T extends string>({ options, selected, onSelect, allLabel = 'Todos' }: ChipSelectRowProps<T>) {
  return (
    <View style={styles.row}>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ selected: selected === undefined }}
        accessibilityLabel={allLabel}
        onPress={() => onSelect(undefined)}
        style={[styles.chip, selected === undefined && styles.chipActive]}
      >
        <Text style={[styles.chipText, selected === undefined && styles.chipTextActive]}>{allLabel}</Text>
      </Pressable>
      {options.map((opt) => {
        const active = selected === opt.value;
        return (
          <Pressable
            key={opt.value}
            accessibilityRole="button"
            accessibilityState={{ selected: active }}
            accessibilityLabel={opt.label}
            onPress={() => onSelect(active ? undefined : opt.value)}
            style={[styles.chip, active && styles.chipActive]}
          >
            <Text style={[styles.chipText, active && styles.chipTextActive]}>{opt.label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: 6,
    paddingHorizontal: 12,
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
  },
  chipTextActive: {
    color: colors.paper,
  },
});
