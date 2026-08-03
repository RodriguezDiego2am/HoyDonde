import React, { useEffect, useRef, useState } from 'react';
import { NativeSyntheticEvent, StyleSheet, Text, TextInput, TextInputKeyPressEventData, View } from 'react-native';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

interface Slot {
  value: string;
  length: number;
  setValue: (value: string) => void;
  ref: React.RefObject<TextInput | null>;
  accessibilityLabel: string;
  testID: string;
}

/** Deja solo dígitos, sin límite de longitud (el recorte lo maneja distributeDigits). */
function onlyDigits(text: string): string {
  return text.replace(/\D/g, '');
}

/**
 * Reparte una tira de dígitos entre los segmentos a partir de `startIndex`, en cascada:
 * llena el segmento actual hasta su longitud máxima y, si sobran dígitos, sigue con el
 * siguiente. Cubre tanto tipear de más (el segmento ya estaba lleno) como pegar un valor
 * largo (fecha completa pegada en el primer segmento).
 */
function distributeDigits(slots: Slot[], startIndex: number, digits: string): void {
  let remaining = digits;
  let idx = startIndex;

  while (idx < slots.length) {
    const chunk = remaining.slice(0, slots[idx].length);
    slots[idx].setValue(chunk);
    remaining = remaining.slice(slots[idx].length);

    if (remaining.length === 0) {
      if (chunk.length === slots[idx].length && idx < slots.length - 1) {
        slots[idx + 1].ref.current?.focus();
      }
      return;
    }
    idx += 1;
  }
}

function handleSlotChange(slots: Slot[], index: number, rawText: string): void {
  const digits = onlyDigits(rawText);
  const slot = slots[index];

  if (digits.length <= slot.length) {
    slot.setValue(digits);
    if (digits.length === slot.length && index < slots.length - 1) {
      slots[index + 1].ref.current?.focus();
    }
    return;
  }

  distributeDigits(slots, index, digits);
}

function handleSlotKeyPress(slots: Slot[], index: number, e: NativeSyntheticEvent<TextInputKeyPressEventData>): void {
  if (e.nativeEvent.key !== 'Backspace' || slots[index].value !== '' || index === 0) return;
  const prev = slots[index - 1];
  prev.setValue(prev.value.slice(0, -1));
  prev.ref.current?.focus();
}

interface DigitBoxProps {
  slot: Slot;
  slots: Slot[];
  index: number;
  hasError: boolean;
}

function DigitBox({ slot, slots, index, hasError }: DigitBoxProps) {
  return (
    <TextInput
      ref={slot.ref}
      testID={slot.testID}
      value={slot.value}
      onChangeText={(text) => handleSlotChange(slots, index, text)}
      onKeyPress={(e) => handleSlotKeyPress(slots, index, e)}
      keyboardType="number-pad"
      placeholder={'•'.repeat(slot.length)}
      placeholderTextColor={colors.inkSoft}
      accessibilityLabel={slot.accessibilityLabel}
      style={[styles.box, slot.length === 4 ? styles.boxWide : styles.boxNarrow, hasError && styles.boxError]}
    />
  );
}

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
 * utils/datetime.ts ya validaba y convertía a UTC — esa lógica no cambia.
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
  box: {
    height: 44,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    textAlign: 'center',
    fontFamily: fonts.medium,
    fontSize: 16,
    color: colors.ink,
    backgroundColor: colors.paper,
  },
  boxNarrow: {
    width: 40,
  },
  boxWide: {
    width: 64,
  },
  boxError: {
    borderColor: colors.error,
    borderWidth: borderWidth.thick,
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    fontSize: 13,
    marginTop: spacing.xs,
  },
});
