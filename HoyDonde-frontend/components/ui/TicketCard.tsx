import React from 'react';
import { StyleSheet, View, ViewStyle } from 'react-native';

import { borderWidth, colors, posterShadow, radii, spacing } from '@/constants/theme';
import { TicketDivider } from '@/components/ui/TicketDivider';

interface TicketCardProps {
  /** Bloque angosto: fecha de un evento o encabezado de una credencial. */
  stub: React.ReactNode;
  /** 'row' pone el talón a la izquierda (tarjetas de evento); 'column' lo pone arriba (pase/credencial). */
  orientation?: 'row' | 'column';
  children: React.ReactNode;
  style?: ViewStyle;
}

/** Entrada troquelada con talón de color y volumen físico, reutilizable en Cartelera y Perfil. */
export function TicketCard({ stub, orientation = 'row', children, style }: TicketCardProps) {
  const isRow = orientation === 'row';

  return (
    <View style={[styles.base, isRow ? styles.baseRow : styles.baseColumn, style]}>
      <View style={isRow ? styles.stubRow : styles.stubColumn}>{stub}</View>
      <TicketDivider orientation={isRow ? 'vertical' : 'horizontal'} />
      <View style={[styles.main, isRow ? styles.mainRow : styles.mainColumn]}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  base: {
    backgroundColor: colors.paperBright,
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderRadius: radii.lg,
    overflow: 'hidden',
    ...posterShadow,
  },
  baseRow: {
    flexDirection: 'row',
    alignItems: 'stretch',
  },
  baseColumn: {
    flexDirection: 'column',
  },
  stubRow: {
    width: 76,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.xs,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.tomato,
  },
  stubColumn: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    backgroundColor: colors.tomato,
  },
  main: {
    flexShrink: 1,
  },
  mainRow: {
    flex: 1,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.md,
  },
  mainColumn: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.md,
  },
});
