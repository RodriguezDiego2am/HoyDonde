import React from 'react';
import { ActivityIndicator, Pressable, PressableProps, StyleSheet, Text } from 'react-native';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

type ActionButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';

interface ActionButtonProps extends Omit<PressableProps, 'style'> {
  label: string;
  variant?: ActionButtonVariant;
  loading?: boolean;
}

const VARIANT_PALETTE: Record<ActionButtonVariant, { background: string; text: string; border: string }> = {
  primary: { background: colors.tomato, text: colors.paper, border: colors.ink },
  secondary: { background: colors.paper, text: colors.ink, border: colors.ink },
  danger: { background: colors.error, text: colors.paper, border: colors.ink },
  ghost: { background: 'transparent', text: colors.error, border: 'transparent' },
};

/** Botón de tinta con desplazamiento corto al presionar (docs/api-mvp-plan.md §6). 'ghost' es texto subrayado sin relleno, para acciones secundarias/destructivas que no deben competir con el CTA principal. */
export function ActionButton({ label, variant = 'primary', loading = false, disabled, ...rest }: ActionButtonProps) {
  const palette = VARIANT_PALETTE[variant];
  const isDisabled = disabled || loading;
  const isGhost = variant === 'ghost';

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled, busy: loading }}
      disabled={isDisabled}
      style={({ pressed }) => [
        isGhost ? styles.ghostBase : styles.base,
        !isGhost && { backgroundColor: palette.background, borderColor: palette.border },
        pressed && !isDisabled && (isGhost ? styles.ghostPressed : styles.pressed),
        isDisabled && styles.disabled,
      ]}
      {...rest}
    >
      {loading ? (
        <ActivityIndicator color={palette.text} />
      ) : (
        <Text style={[isGhost ? styles.ghostLabel : styles.label, { color: palette.text }]}>{label}</Text>
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
  ghostBase: {
    alignSelf: 'center',
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.sm,
  },
  ghostPressed: {
    opacity: 0.6,
  },
  ghostLabel: {
    fontFamily: fonts.semiBold,
    fontSize: 14,
    letterSpacing: 0.3,
    textTransform: 'uppercase',
    textDecorationLine: 'underline',
  },
});
