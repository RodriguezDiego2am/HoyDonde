import React from 'react';
import { StyleSheet, View, ViewProps } from 'react-native';

import { borderWidth, colors, radii } from '@/constants/theme';

type SurfaceVariant = 'paper' | 'sand';

interface SurfaceProps extends ViewProps {
  variant?: SurfaceVariant;
}

/** Superficie tipo papel con borde de tinta firme, sin sombra difusa. */
export function Surface({ variant = 'paper', style, children, ...rest }: SurfaceProps) {
  return (
    <View
      style={[styles.base, { backgroundColor: variant === 'paper' ? colors.paper : colors.sand }, style]}
      {...rest}
    >
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  base: {
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.md,
    padding: 16,
  },
});
