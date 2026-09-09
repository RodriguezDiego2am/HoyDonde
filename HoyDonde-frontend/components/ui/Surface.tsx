import React from 'react';
import { StyleSheet, View, ViewProps } from 'react-native';

import { borderWidth, colors, posterShadow, radii, spacing } from '@/constants/theme';

type SurfaceVariant = 'paper' | 'sand' | 'ink' | 'cobalt';

interface SurfaceProps extends ViewProps {
  variant?: SurfaceVariant;
}

/** Bloque editorial reutilizable, con borde firme y una elevación corta tipo afiche. */
export function Surface({ variant = 'paper', style, children, ...rest }: SurfaceProps) {
  return (
    <View style={[styles.base, { backgroundColor: BACKGROUND_COLORS[variant] }, style]} {...rest}>
      {children}
    </View>
  );
}

const BACKGROUND_COLORS: Record<SurfaceVariant, string> = {
  paper: colors.paperBright,
  sand: colors.sand,
  ink: colors.ink,
  cobalt: colors.cobalt,
};

const styles = StyleSheet.create({
  base: {
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderRadius: radii.lg,
    padding: spacing.md,
    ...posterShadow,
  },
});
