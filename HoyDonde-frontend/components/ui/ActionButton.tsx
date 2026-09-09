import React from 'react';
import { ActivityIndicator, Pressable, PressableProps, StyleSheet, Text } from 'react-native';

import { borderWidth, colors, fonts, posterShadow, radii, spacing } from '@/constants/theme';

type ActionButtonVariant = 'primary' | 'secondary' | 'lime' | 'danger' | 'ghost';

interface ActionButtonProps extends Omit<PressableProps, 'style'> {
  label: string;
  variant?: ActionButtonVariant;
  loading?: boolean;
  selected?: boolean;
}

const VARIANT_PALETTE: Record<ActionButtonVariant, { background: string; text: string; border: string }> = {
  primary: { background: colors.tomato, text: colors.paperBright, border: colors.ink },
  secondary: { background: colors.paperBright, text: colors.ink, border: colors.ink },
  lime: { background: colors.lime, text: colors.ink, border: colors.ink },
  danger: { background: colors.error, text: colors.paperBright, border: colors.ink },
  ghost: { background: 'transparent', text: colors.error, border: 'transparent' },
};

/** CTA de afiche: volumen corto, etiqueta visible y área táctil de al menos 48 px. */
export function ActionButton({ label, variant = 'primary', loading = false, disabled, selected, ...rest }: ActionButtonProps) {
  const palette = VARIANT_PALETTE[variant];
  const isDisabled = disabled || loading;
  const isGhost = variant === 'ghost';

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled, busy: loading, selected }}
      accessibilityLabel={loading ? `${label}, cargando` : undefined}
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
    minHeight: 48,
    borderWidth: borderWidth.thick,
    borderRadius: radii.md,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    alignItems: 'center',
    justifyContent: 'center',
    ...posterShadow,
  },
  pressed: {
    transform: [{ translateX: 3 }, { translateY: 4 }],
    shadowOpacity: 0,
    elevation: 0,
  },
  disabled: {
    opacity: 0.5,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 14,
    letterSpacing: 0.7,
    textTransform: 'uppercase',
    textAlign: 'center',
  },
  ghostBase: {
    minHeight: 44,
    alignSelf: 'center',
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    justifyContent: 'center',
  },
  ghostPressed: {
    opacity: 0.6,
  },
  ghostLabel: {
    fontFamily: fonts.semiBold,
    fontSize: 13,
    letterSpacing: 0.4,
    textTransform: 'uppercase',
    textDecorationLine: 'underline',
  },
});
