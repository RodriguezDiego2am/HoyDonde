import React from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { colors, fonts, spacing } from '@/constants/theme';

type IconName = React.ComponentProps<typeof MaterialIcons>['name'];

interface AsyncStateViewProps {
  variant: 'loading' | 'error' | 'empty';
  message: string;
  hint?: string;
  icon?: IconName;
  retryLabel?: string;
  onRetry?: () => void;
}

/** Bloque compartido de carga/error/vacío con reintento, reutilizado por las listas de Organización y Control. */
export function AsyncStateView({ variant, message, hint, icon, retryLabel = 'Reintentar', onRetry }: AsyncStateViewProps) {
  if (variant === 'loading') {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.tomato} />
      </View>
    );
  }

  if (variant === 'error') {
    return (
      <View style={styles.center}>
        <Text style={styles.errorText}>{message}</Text>
        {hint ? <Text style={styles.hint}>{hint}</Text> : null}
        {onRetry ? (
          <View style={styles.retryButton}>
            <ActionButton label={retryLabel} variant="secondary" onPress={onRetry} />
          </View>
        ) : null}
      </View>
    );
  }

  return (
    <View style={styles.center}>
      <MaterialIcons name={icon ?? 'inbox'} size={36} color={colors.inkSoft} />
      <Text style={styles.emptyText}>{message}</Text>
      {hint ? <Text style={styles.hint}>{hint}</Text> : null}
      {onRetry ? (
        <View style={styles.retryButton}>
          <ActionButton label={retryLabel} variant="secondary" onPress={onRetry} />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  center: {
    flex: 1,
    padding: spacing.lg,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    textAlign: 'center',
  },
  emptyText: {
    fontFamily: fonts.bold,
    fontSize: 16,
    color: colors.ink,
    textAlign: 'center',
  },
  hint: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
    textAlign: 'center',
  },
  retryButton: {
    marginTop: spacing.sm,
    alignSelf: 'stretch',
  },
});
