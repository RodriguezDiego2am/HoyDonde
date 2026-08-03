import React from 'react';
import { StyleSheet, Text, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';

interface ReportLedgerRowProps {
  label: string;
  value: string;
  emphasis?: boolean;
}

/** Fila de "libro mayor" (label a la izquierda, cifra alineada a la derecha) — firma visual del módulo de reportes: hairline entre filas, cifra destacada en tomato cuando emphasis. */
export function ReportLedgerRow({ label, value, emphasis }: ReportLedgerRowProps) {
  return (
    <View style={styles.row}>
      <Text style={styles.label}>{label}</Text>
      <Text style={[styles.value, emphasis && styles.valueEmphasis]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    paddingVertical: spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: colors.sand,
  },
  label: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.inkSoft,
  },
  value: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
  },
  valueEmphasis: {
    color: colors.tomato,
    fontSize: 18,
  },
});
