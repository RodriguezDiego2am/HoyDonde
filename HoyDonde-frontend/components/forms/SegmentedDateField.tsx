import React, { useEffect, useRef, useState } from 'react';
import { StyleSheet, Text, TextInput, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';
import { DigitBox } from './DigitBox';
import { Slot } from './segmentedDigits';

interface SegmentedDateFieldProps {
  /** Etiqueta del campo (p. ej. "Desde", "Hasta"); prefija las etiquetas de accesibilidad de cada segmento. */
  label: string;
  /** "DD/MM/AAAA", igual formato que utils/datetime.ts parseLocalDate espera. */
  value: string;
  onChange: (value: string) => void;
  error?: string | null;
  testIDPrefix: string;
}

/**
 * Variante de solo fecha (DD / MM / AAAA) de SegmentedDateTimeField, para filtros donde la hora
 * no tiene sentido (rango Desde/Hasta de la Cartelera: describen un día completo, no un
 * instante). Comparte la mecánica de segmentos (components/forms/segmentedDigits.ts, DigitBox)
 * con la variante con hora en vez de duplicar el parser, y nunca pide ni fabrica un "00:00".
 */
export function SegmentedDateField({ label, value, onChange, error, testIDPrefix }: SegmentedDateFieldProps) {
  const [day, setDay] = useState('');
  const [month, setMonth] = useState('');
  const [year, setYear] = useState('');

  const dayRef = useRef<TextInput>(null);
  const monthRef = useRef<TextInput>(null);
  const yearRef = useRef<TextInput>(null);

  // Precarga: solo aplica un valor externo mientras el usuario todavía no tipeó nada acá, para
  // no pelear con lo que está escribiendo.
  useEffect(() => {
    if (day !== '' || month !== '' || year !== '') return;
    const [d, m, y] = value.split('/');
    if (d && m && y) {
      setDay(d);
      setMonth(m);
      setYear(y);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  useEffect(() => {
    onChange(`${day}/${month}/${year}`);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [day, month, year]);

  const slots: Slot[] = [
    { value: day, length: 2, setValue: setDay, ref: dayRef, accessibilityLabel: `${label} - día`, testID: `${testIDPrefix}-day` },
    { value: month, length: 2, setValue: setMonth, ref: monthRef, accessibilityLabel: `${label} - mes`, testID: `${testIDPrefix}-month` },
    { value: year, length: 4, setValue: setYear, ref: yearRef, accessibilityLabel: `${label} - año`, testID: `${testIDPrefix}-year` },
  ];

  const hasError = Boolean(error);

  return (
    <View style={styles.container}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.row}>
        {slots.map((slot, index) => (
          <React.Fragment key={slot.testID}>
            {index > 0 ? <Text style={styles.separator}>/</Text> : null}
            <DigitBox slot={slot} slots={slots} index={index} hasError={hasError} />
          </React.Fragment>
        ))}
      </View>
      {error ? (
        <Text style={styles.errorText} accessibilityRole="alert" accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    marginBottom: spacing.md,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    marginBottom: spacing.xs,
    color: colors.inkSoft,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  separator: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.inkSoft,
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    fontSize: 13,
    marginTop: spacing.xs,
  },
});
