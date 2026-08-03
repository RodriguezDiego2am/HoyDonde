import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import FormInput from '@/components/FormInput';

/** Borrador de un tipo de ticket en el formulario: valores como texto (controlados por TextInput), parseados a número recién al armar el TicketGroupDto real. */
export interface TicketGroupDraft {
  key: string;
  nombre: string;
  precio: string;
  cantidadDisponible: string;
}

let ticketGroupKeySeq = 0;

export function createEmptyTicketGroupDraft(): TicketGroupDraft {
  ticketGroupKeySeq += 1;
  return { key: `tg-${ticketGroupKeySeq}`, nombre: '', precio: '', cantidadDisponible: '' };
}

export interface TicketGroupFieldErrors {
  nombre?: string;
  precio?: string;
  cantidadDisponible?: string;
}

interface TicketGroupsFieldProps {
  value: TicketGroupDraft[];
  onChange: (next: TicketGroupDraft[]) => void;
  /** Errores por índice del array, en el mismo orden que `value`. */
  errors?: (TicketGroupFieldErrors | undefined)[];
}

/** Editor de tipos de entrada (nombre/precio/cantidad), usado al crear y editar un evento — reemplaza siempre la colección completa (CLAUDE.md: "Ticket lifecycle"). */
export function TicketGroupsField({ value, onChange, errors }: TicketGroupsFieldProps) {
  const updateAt = (index: number, patch: Partial<TicketGroupDraft>) => {
    const next = value.map((tg, i) => (i === index ? { ...tg, ...patch } : tg));
    onChange(next);
  };

  const removeAt = (index: number) => {
    onChange(value.filter((_, i) => i !== index));
  };

  const addRow = () => {
    onChange([...value, createEmptyTicketGroupDraft()]);
  };

  return (
    <View style={styles.container}>
      {value.map((tg, index) => {
        const rowErrors = errors?.[index];
        return (
          <Surface key={tg.key} variant="sand" style={styles.row}>
            <View style={styles.rowHeader}>
              <Text style={styles.rowLabel}>Tipo de entrada {index + 1}</Text>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={`Quitar tipo de entrada ${index + 1}`}
                onPress={() => removeAt(index)}
                hitSlop={8}
              >
                <MaterialIcons name="close" size={18} color={colors.error} />
              </Pressable>
            </View>

            <FormInput
              label="Nombre"
              value={tg.nombre}
              onChangeText={(text) => updateAt(index, { nombre: text })}
              placeholder="General, VIP..."
              error={rowErrors?.nombre}
              autoCapitalize="words"
            />
            <View style={styles.rowFields}>
              <View style={styles.rowField}>
                <FormInput
                  label="Precio"
                  value={tg.precio}
                  onChangeText={(text) => updateAt(index, { precio: text })}
                  placeholder="0"
                  keyboardType="numeric"
                  error={rowErrors?.precio}
                />
              </View>
              <View style={styles.rowField}>
                <FormInput
                  label="Stock"
                  value={tg.cantidadDisponible}
                  onChangeText={(text) => updateAt(index, { cantidadDisponible: text })}
                  placeholder="0"
                  keyboardType="numeric"
                  error={rowErrors?.cantidadDisponible}
                />
              </View>
            </View>
          </Surface>
        );
      })}

      <ActionButton label="+ Agregar tipo de entrada" variant="secondary" onPress={addRow} />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: spacing.md,
  },
  row: {
    gap: spacing.xs,
  },
  rowHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: spacing.xs,
  },
  rowLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    color: colors.inkSoft,
  },
  rowFields: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  rowField: {
    flex: 1,
  },
});
