import React from 'react';
import { StyleSheet, Text, View } from 'react-native';

import { colors, fonts, radii } from '@/constants/theme';

export type StampTone = 'ink' | 'success' | 'error' | 'cobalt';

interface StatusStampProps {
  label: string;
  tone?: StampTone;
}

export const TONE_COLOR: Record<StampTone, string> = {
  ink: colors.ink,
  success: colors.success,
  error: colors.error,
  cobalt: colors.cobalt,
};

/** Sello de estado: nunca depende solo del color, el texto siempre es explícito. */
export function StatusStamp({ label, tone = 'ink' }: StatusStampProps) {
  const tint = TONE_COLOR[tone];
  return (
    <View style={[styles.base, { borderColor: tint }]}>
      <Text style={[styles.label, { color: tint }]}>{label.toUpperCase()}</Text>
    </View>
  );
}

export function eventEstadoTone(estado: string): StampTone {
  switch (estado) {
    case 'Publicado':
      return 'success';
    case 'Cancelado':
      return 'error';
    case 'Finalizado':
      return 'cobalt';
    default:
      return 'ink';
  }
}

const styles = StyleSheet.create({
  base: {
    alignSelf: 'flex-start',
    borderWidth: 2,
    borderRadius: radii.sm,
    paddingVertical: 2,
    paddingHorizontal: 8,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1,
  },
});
