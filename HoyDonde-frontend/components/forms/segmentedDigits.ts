import React from 'react';
import { NativeSyntheticEvent, TextInput, TextInputKeyPressEventData } from 'react-native';

/**
 * Lógica compartida de "segmentos de dígitos" (SegmentedDateTimeField, SegmentedDateField): solo
 * se tipean dígitos, los separadores ("/", ":") son fijos y el foco avanza/retrocede solo. Vive
 * separada de los componentes que la usan para no duplicar el parser entre la variante con hora
 * y la variante de solo fecha.
 */

export interface Slot {
  value: string;
  length: number;
  setValue: (value: string) => void;
  ref: React.RefObject<TextInput | null>;
  accessibilityLabel: string;
  testID: string;
}

/** Deja solo dígitos, sin límite de longitud (el recorte lo maneja distributeDigits). */
export function onlyDigits(text: string): string {
  return text.replace(/\D/g, '');
}

/**
 * Reparte una tira de dígitos entre los segmentos a partir de `startIndex`, en cascada:
 * llena el segmento actual hasta su longitud máxima y, si sobran dígitos, sigue con el
 * siguiente. Cubre tanto tipear de más (el segmento ya estaba lleno) como pegar un valor
 * largo (fecha completa pegada en el primer segmento).
 */
export function distributeDigits(slots: Slot[], startIndex: number, digits: string): void {
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

export function handleSlotChange(slots: Slot[], index: number, rawText: string): void {
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

export function handleSlotKeyPress(slots: Slot[], index: number, e: NativeSyntheticEvent<TextInputKeyPressEventData>): void {
  if (e.nativeEvent.key !== 'Backspace' || slots[index].value !== '' || index === 0) return;
  const prev = slots[index - 1];
  prev.setValue(prev.value.slice(0, -1));
  prev.ref.current?.focus();
}
