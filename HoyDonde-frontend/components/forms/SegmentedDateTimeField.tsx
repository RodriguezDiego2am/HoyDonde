import React, { useEffect, useRef, useState } from 'react';
import { StyleSheet, Text, TextInput, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';
import { DigitBox } from './DigitBox';
import { Slot } from './segmentedDigits';

interface SegmentedDateTimeFieldProps {
  /** Etiqueta del bloque completo (p. ej. "Inicio", "Fin"); prefija las etiquetas de accesibilidad de cada segmento. */
  label: string;
  /** "DD/MM/AAAA", igual formato que utils/datetime.ts parseLocalDateTime espera. */
  dateValue: string;
  /** "HH:MM", igual formato que utils/datetime.ts parseLocalDateTime espera. */
  timeValue: string;
  onChangeDate: (value: string) => void;
  onChangeTime: (value: string) => void;
  error?: string | null;
  testIDPrefix: string;
}

/**
 * Fecha y hora con segmentos visuales (DD / MM / AAAA y HH : MM): solo se tipean dígitos, los
 * separadores son fijos. Reemplaza los inputs de texto libre que exigían escribir "/" y ":" a
 * mano. Sigue produciendo/consumiendo los mismos strings "DD/MM/AAAA"/"HH:MM" que
 * utils/datetime.ts ya validaba y convertía a UTC — esa lógica no cambia. La variante de solo
 * fecha (sin hora, para los filtros de rango de la Cartelera) es SegmentedDateField, que
 * comparte esta misma mecánica de segmentos (components/forms/segmentedDigits.ts, DigitBox) en
 * vez de duplicarla.
 */
export function SegmentedDateTimeField({
  label,
  dateValue,
  timeValue,
  onChangeDate,
  onChangeTime,
  error,
  testIDPrefix,
}: SegmentedDateTimeFieldProps) {
  const [day, setDay] = useState('');
  const [month, setMonth] = useState('');
  const [year, setYear] = useState('');
  const [hour, setHour] = useState('');
  const [minute, setMinute] = useState('');

  const dayRef = useRef<TextInput>(null);
  const monthRef = useRef<TextInput>(null);
  const yearRef = useRef<TextInput>(null);
  const hourRef = useRef<TextInput>(null);
  const minuteRef = useRef<TextInput>(null);

  // Precarga (p. ej. edición de un evento existente): solo aplica un valor externo mientras el
  // usuario todavía no tipeó nada acá, para no pelear con lo que está escribiendo.
  useEffect(() => {
    if (day !== '' || month !== '' || year !== '') return;
    const [d, m, y] = dateValue.split('/');
    if (d && m && y) {
      setDay(d);
      setMonth(m);
      setYear(y);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dateValue]);

  useEffect(() => {
    if (hour !== '' || minute !== '') return;
    const [h, min] = timeValue.split(':');
    if (h && min) {
      setHour(h);
      setMinute(min);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [timeValue]);

  useEffect(() => {
    onChangeDate(`${day}/${month}/${year}`);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [day, month, year]);

  useEffect(() => {
    onChangeTime(`${hour}:${minute}`);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hour, minute]);

  const dateSlots: Slot[] = [
    { value: day, length: 2, setValue: setDay, ref: dayRef, accessibilityLabel: `${label} - día`, testID: `${testIDPrefix}-day` },
    { value: month, length: 2, setValue: setMonth, ref: monthRef, accessibilityLabel: `${label} - mes`, testID: `${testIDPrefix}-month` },
    { value: year, length: 4, setValue: setYear, ref: yearRef, accessibilityLabel: `${label} - año`, testID: `${testIDPrefix}-year` },
  ];
  const timeSlots: Slot[] = [
    { value: hour, length: 2, setValue: setHour, ref: hourRef, accessibilityLabel: `${label} - hora`, testID: `${testIDPrefix}-hour` },
    { value: minute, length: 2, setValue: setMinute, ref: minuteRef, accessibilityLabel: `${label} - minutos`, testID: `${testIDPrefix}-minute` },
  ];

  const hasError = Boolean(error);

  return (
    <View style={styles.container}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.row}>
        <View style={styles.group}>
          {dateSlots.map((slot, index) => (
            <React.Fragment key={slot.testID}>
              {index > 0 ? <Text style={styles.separator}>/</Text> : null}
              <DigitBox slot={slot} slots={dateSlots} index={index} hasError={hasError} />
            </React.Fragment>
          ))}
        </View>

        <View style={styles.groupGap} />

        <View style={styles.group}>
          {timeSlots.map((slot, index) => (
            <React.Fragment key={slot.testID}>
              {index > 0 ? <Text style={styles.separator}>:</Text> : null}
              <DigitBox slot={slot} slots={timeSlots} index={index} hasError={hasError} />
            </React.Fragment>
          ))}
        </View>
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
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
  group: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  groupGap: {
    width: spacing.md,
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
