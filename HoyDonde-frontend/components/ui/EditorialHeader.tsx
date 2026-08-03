import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { colors, fonts, spacing } from '@/constants/theme';

interface EditorialHeaderProps {
  eyebrow: string;
  title: string;
  subtitle?: string;
  /** Las pantallas fuera de las tabs no tienen header nativo (Stack global con headerShown: false): esto agrega el "← Volver" editorial. */
  showBack?: boolean;
  onRefresh?: () => void;
}

/** Encabezado editorial compartido por las pantallas de Administración/Organización/Control: masthead + hairline, igual criterio visual que Cartelera y Mis entradas. */
export function EditorialHeader({ eyebrow, title, subtitle, showBack, onRefresh }: EditorialHeaderProps) {
  return (
    <View style={styles.header}>
      {showBack ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Volver"
          onPress={() => router.back()}
          hitSlop={10}
          style={({ pressed }) => [styles.backRow, pressed && styles.backRowPressed]}
        >
          <MaterialIcons name="arrow-back" size={16} color={colors.ink} />
          <Text style={styles.backLabel}>Volver</Text>
        </Pressable>
      ) : null}

      <View style={styles.topRow}>
        <Text style={styles.eyebrow}>{eyebrow}</Text>
        {onRefresh ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Actualizar"
            onPress={onRefresh}
            hitSlop={10}
            style={({ pressed }) => [styles.reloadButton, pressed && styles.reloadButtonPressed]}
          >
            <MaterialIcons name="refresh" size={18} color={colors.ink} />
          </Pressable>
        ) : null}
      </View>
      <Text style={styles.title}>{title}</Text>
      {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.xxl,
    paddingBottom: spacing.md,
    borderBottomWidth: 3,
    borderBottomColor: colors.ink,
  },
  backRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    alignSelf: 'flex-start',
    marginBottom: spacing.sm,
  },
  backRowPressed: {
    opacity: 0.6,
  },
  backLabel: {
    fontFamily: fonts.bold,
    fontSize: 13,
    letterSpacing: 0.5,
    color: colors.ink,
    textTransform: 'uppercase',
  },
  topRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  eyebrow: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1.2,
    color: colors.inkSoft,
  },
  reloadButton: {
    borderWidth: 1,
    borderColor: colors.ink,
    borderRadius: 14,
    width: 28,
    height: 28,
    alignItems: 'center',
    justifyContent: 'center',
  },
  reloadButtonPressed: {
    backgroundColor: colors.sand,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 30,
    color: colors.ink,
    marginTop: spacing.sm,
  },
  subtitle: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    marginTop: 2,
  },
});
