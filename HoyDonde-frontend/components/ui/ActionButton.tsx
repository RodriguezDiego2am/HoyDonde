import React from 'react';
import { ActivityIndicator, Pressable, PressableProps, StyleSheet, Text } from 'react-native';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

type ActionButtonVariant = 'primary' | 'secondary' | 'danger';

interface ActionButtonProps extends Omit<PressableProps, 'style'> {
  label: string;
  variant?: ActionButtonVariant;
  loading?: boolean;
}

const VARIANT_PALETTE: Record<ActionButtonVariant, { background: string; text: string; border: string }> = {
  primary: { background: colors.tomato, text: colors.paper, border: colors.ink },
  secondary: { background: colors.paper, text: colors.ink, border: colors.ink },
  danger: { background: colors.error, text: colors.paper, border: colors.ink },
};

/** Botón de tinta con desplazamiento corto al presionar (docs/api-mvp-plan.md §6). */
export function ActionButton({ label, variant = 'primary', loading = false, disabled, ...rest }: ActionButtonProps) {
  const palette = VARIANT_PALETTE[variant];
  const isDisabled = disabled || loading;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled, busy: loading }}
      disabled={isDisabled}
      style={({ pressed }) => [
        styles.base,
        { backgroundColor: palette.background, borderColor: palette.border },
        pressed && !isDisabled && styles.pressed,
        isDisabled && styles.disabled,
      ]}
      {...rest}
    >
      {loading ? (
        <ActivityIndicator color={palette.text} />
      ) : (
        <Text style={[styles.label, { color: palette.text }]}>{label}</Text>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    borderWidth: borderWidth.thick,
    borderRadius: radii.sm,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pressed: {
    transform: [{ translateX: 2 }, { translateY: 2 }],
  },
  disabled: {
    opacity: 0.5,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 16,
    letterSpacing: 0.5,
    textTransform: 'uppercase',
  },
});
