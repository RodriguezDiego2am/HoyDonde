import React from 'react';
import { Modal, StyleSheet, Text, View } from 'react-native';

import { ActionButton } from '@/components/ui/ActionButton';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';

interface ConfirmDialogProps {
  visible: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
  /** 'danger' para acciones destructivas/irreversibles (p. ej. cancelar un evento). */
  variant?: 'primary' | 'danger';
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Diálogo de confirmación editorial reutilizado antes de publicar/cancelar un evento (docs/api-mvp-plan.md §2). */
export function ConfirmDialog({
  visible,
  title,
  message,
  confirmLabel,
  cancelLabel = 'Cancelar',
  variant = 'primary',
  loading = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onCancel}>
      <View style={styles.scrim}>
        <View style={styles.card}>
          <Text style={styles.title}>{title}</Text>
          <Text style={styles.message}>{message}</Text>

          <View style={styles.actions}>
            <View style={styles.actionButton}>
              <ActionButton label={cancelLabel} variant="ghost" onPress={onCancel} disabled={loading} />
            </View>
            <View style={styles.actionButton}>
              <ActionButton
                label={confirmLabel}
                variant={variant === 'danger' ? 'danger' : 'primary'}
                onPress={onConfirm}
                loading={loading}
              />
            </View>
          </View>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  scrim: {
    flex: 1,
    backgroundColor: 'rgba(23, 21, 18, 0.72)',
    alignItems: 'center',
    justifyContent: 'center',
    padding: spacing.lg,
  },
  card: {
    width: '100%',
    maxWidth: 360,
    backgroundColor: colors.paper,
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderRadius: radii.md,
    padding: spacing.lg,
  },
  title: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.ink,
  },
  message: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    marginTop: spacing.sm,
    lineHeight: 20,
  },
  actions: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.lg,
  },
  actionButton: {
    flex: 1,
  },
});
