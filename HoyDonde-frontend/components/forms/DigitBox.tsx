import React from 'react';
import { StyleSheet, TextInput } from 'react-native';

import { borderWidth, colors, fonts, radii } from '@/constants/theme';
import { handleSlotChange, handleSlotKeyPress, Slot } from './segmentedDigits';

interface DigitBoxProps {
  slot: Slot;
  slots: Slot[];
  index: number;
  hasError: boolean;
}

/** Un único casillero de dígito de un campo segmentado (día/mes/año/hora/minuto). */
export function DigitBox({ slot, slots, index, hasError }: DigitBoxProps) {
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

const styles = StyleSheet.create({
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
});
