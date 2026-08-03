import React from 'react';
import { Modal, Platform, Pressable, ScrollView, StyleSheet, Text, View, KeyboardAvoidingView } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import FormInput from '@/components/FormInput';
import { SegmentedDateField } from '@/components/forms/SegmentedDateField';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

export interface CategoriaOption {
  value: string;
  label: string;
}

interface EventFilterPanelProps {
  visible: boolean;
  onClose: () => void;
  categorias: CategoriaOption[];
  categoria: string | undefined;
  onChangeCategoria: (value: string | undefined) => void;
  ubicacion: string;
  onChangeUbicacion: (value: string) => void;
  fechaDesde: string;
  onChangeFechaDesde: (value: string) => void;
  fechaHasta: string;
  onChangeFechaHasta: (value: string) => void;
  error?: string | null;
  onApply: () => void;
  onClear: () => void;
}

/**
 * Panel editorial compacto de filtros de la Cartelera pública (docs/api-mvp-plan.md §7 Frontend
 * 5). Componente puramente controlado: no valida ni convierte fechas por sí mismo, solo expone
 * los campos y delega "Aplicar"/"Limpiar" a quien lo usa (app/(tabs)/index.tsx), que es quien
 * conoce el contrato real de GET /api/events (utils/datetime.ts) y decide si el rango es válido
 * antes de llamar a la API.
 */
export function EventFilterPanel({
  visible,
  onClose,
  categorias,
  categoria,
  onChangeCategoria,
  ubicacion,
  onChangeUbicacion,
  fechaDesde,
  onChangeFechaDesde,
  fechaHasta,
  onChangeFechaHasta,
  error,
  onApply,
  onClear,
}: EventFilterPanelProps) {
  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <View style={styles.backdrop}>
        <Pressable
          style={StyleSheet.absoluteFill}
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          onPress={onClose}
        />
        <KeyboardAvoidingView
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          style={styles.sheetWrap}
        >
          <View style={styles.sheet} testID="event-filter-panel">
            <View style={styles.headerRow}>
              <Text style={styles.title}>Filtros</Text>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Cerrar filtros"
                onPress={onClose}
                hitSlop={10}
                style={({ pressed }) => [styles.closeButton, pressed && styles.closeButtonPressed]}
              >
                <MaterialIcons name="close" size={20} color={colors.ink} />
              </Pressable>
            </View>

            <ScrollView
              contentContainerStyle={styles.scrollContent}
              keyboardShouldPersistTaps="handled"
              showsVerticalScrollIndicator={false}
            >
              <View style={styles.dateRow}>
                <View style={styles.dateField}>
                  <SegmentedDateField
                    label="Desde"
                    value={fechaDesde}
                    onChange={onChangeFechaDesde}
                    testIDPrefix="filtro-fecha-desde"
                  />
                </View>
                <View style={styles.dateField}>
                  <SegmentedDateField
                    label="Hasta"
                    value={fechaHasta}
                    onChange={onChangeFechaHasta}
                    testIDPrefix="filtro-fecha-hasta"
                  />
                </View>
              </View>
              {error ? (
                <Text style={styles.errorText} accessibilityRole="alert" accessibilityLiveRegion="polite">
                  {error}
                </Text>
              ) : null}

              <Text style={styles.sectionLabel}>Categoría</Text>
              <View style={styles.chipRow}>
                {categorias.map((item) => {
                  const active = categoria === item.value;
                  return (
                    <Pressable
                      key={item.value}
                      accessibilityRole="button"
                      accessibilityState={{ selected: active }}
                      accessibilityLabel={`Filtrar por ${item.label}`}
                      onPress={() => onChangeCategoria(active ? undefined : item.value)}
                      style={[styles.chip, active && styles.chipActive]}
                    >
                      <Text style={[styles.chipText, active && styles.chipTextActive]}>{item.label}</Text>
                    </Pressable>
                  );
                })}
              </View>

              <FormInput
                label="Ubicación"
                value={ubicacion}
                onChangeText={onChangeUbicacion}
                placeholder="Ej: Parque Central"
              />
            </ScrollView>

            <View style={styles.footer}>
              <View style={styles.footerButton}>
                <ActionButton label="Limpiar" variant="secondary" onPress={onClear} />
              </View>
              <View style={styles.footerButton}>
                <ActionButton label="Aplicar filtros" onPress={onApply} />
              </View>
            </View>
          </View>
        </KeyboardAvoidingView>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: 'rgba(23, 21, 18, 0.5)',
    justifyContent: 'flex-end',
  },
  sheetWrap: {
    maxHeight: '85%',
  },
  sheet: {
    backgroundColor: colors.paper,
    borderTopWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderTopLeftRadius: radii.lg,
    borderTopRightRadius: radii.lg,
    paddingTop: spacing.md,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.lg,
  },
  headerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: spacing.md,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 22,
    color: colors.ink,
  },
  closeButton: {
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    width: 32,
    height: 32,
    alignItems: 'center',
    justifyContent: 'center',
  },
  closeButtonPressed: {
    backgroundColor: colors.sand,
  },
  scrollContent: {
    paddingBottom: spacing.sm,
  },
  dateRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  dateField: {
    flex: 1,
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    fontSize: 13,
    marginTop: -spacing.sm,
    marginBottom: spacing.sm,
  },
  sectionLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    marginBottom: spacing.xs,
    color: colors.inkSoft,
  },
  chipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginBottom: spacing.md,
  },
  chip: {
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: 6,
    paddingHorizontal: 12,
  },
  chipActive: {
    backgroundColor: colors.ink,
  },
  chipText: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.ink,
    textTransform: 'uppercase',
  },
  chipTextActive: {
    color: colors.paper,
  },
  footer: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  footerButton: {
    flex: 1,
  },
});
