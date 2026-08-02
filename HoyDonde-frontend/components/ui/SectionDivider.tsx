import React from 'react';
import { StyleSheet, Text, View, ViewStyle } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';

interface SectionDividerProps {
  index: string;
  label: string;
  style?: ViewStyle;
}

/** Separador editorial numerado entre bloques de un formulario largo (no crea pasos/wizard, solo agrupa visualmente). */
export function SectionDivider({ index, label, style }: SectionDividerProps) {
  return (
    <View style={[styles.row, style]}>
      <Text style={styles.index}>{index}</Text>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.rule} />
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: spacing.lg,
    marginBottom: spacing.md,
    gap: spacing.sm,
  },
  index: {
    fontFamily: fonts.black,
    fontSize: 20,
    color: colors.tomato,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 13,
    letterSpacing: 1,
    color: colors.ink,
    textTransform: 'uppercase',
  },
  rule: {
    flex: 1,
    height: 1,
    backgroundColor: colors.ink,
    opacity: 0.25,
    marginLeft: spacing.xs,
  },
});
