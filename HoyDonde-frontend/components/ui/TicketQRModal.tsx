import React from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { buildTicketQrPayload } from '@/utils/ticketQr';

interface TicketQRModalProps {
  visible: boolean;
  ticketId: string;
  eventId: string;
  eventoNombre: string;
  onClose: () => void;
}

/**
 * QR sin firma ni validación offline (el backend no las implementa): solo
 * codifica { ticketId, eventId } vía buildTicketQrPayload, la misma forma
 * documentada en utils/ticketQr.ts para que Frontend 3 (Control) reutilice el
 * parser. Los ids se muestran también como texto seleccionable, como
 * fallback operativo si el QR no se puede escanear.
 */
export function TicketQRModal({ visible, ticketId, eventId, eventoNombre, onClose }: TicketQRModalProps) {
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <View style={styles.scrim}>
        <View style={styles.card}>
          <Text style={styles.eyebrow}>ENTRADA DIGITAL</Text>
          <Text style={styles.eventName} numberOfLines={2}>
            {eventoNombre}
          </Text>

          <View style={styles.qrFrame}>
            <QRCode value={buildTicketQrPayload(ticketId, eventId)} size={200} color={colors.ink} backgroundColor={colors.paper} />
          </View>

          <Text style={styles.hint}>Mostrá este código al Control. No tiene firma digital: se valida contra el sistema.</Text>

          <View style={styles.idsBlock}>
            <View style={styles.idRow}>
              <Text style={styles.idLabel}>TICKET</Text>
              <Text selectable style={styles.idValue}>
                {ticketId}
              </Text>
            </View>
            <View style={styles.idRow}>
              <Text style={styles.idLabel}>EVENTO</Text>
              <Text selectable style={styles.idValue}>
                {eventId}
              </Text>
            </View>
          </View>
          <Text style={styles.copyHint}>Mantené presionado un id para copiarlo.</Text>

          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Cerrar código QR"
            onPress={onClose}
            style={({ pressed }) => [styles.closeButton, pressed && styles.closeButtonPressed]}
          >
            <Text style={styles.closeButtonLabel}>Cerrar</Text>
          </Pressable>
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
    maxWidth: 340,
    backgroundColor: colors.paper,
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderRadius: radii.md,
    padding: spacing.lg,
    alignItems: 'center',
  },
  eyebrow: {
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 1.2,
    color: colors.tomato,
  },
  eventName: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.ink,
    textAlign: 'center',
    marginTop: spacing.xs,
  },
  qrFrame: {
    marginTop: spacing.lg,
    padding: spacing.md,
    backgroundColor: colors.paper,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
  },
  hint: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    textAlign: 'center',
    marginTop: spacing.md,
  },
  idsBlock: {
    alignSelf: 'stretch',
    marginTop: spacing.md,
    gap: spacing.xs,
  },
  idRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  idLabel: {
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 0.8,
    color: colors.inkSoft,
  },
  idValue: {
    fontFamily: fonts.medium,
    fontSize: 12,
    color: colors.ink,
    flexShrink: 1,
    textAlign: 'right',
  },
  copyHint: {
    fontFamily: fonts.regular,
    fontSize: 11,
    color: colors.inkSoft,
    marginTop: spacing.xs,
  },
  closeButton: {
    marginTop: spacing.lg,
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    backgroundColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.xl,
  },
  closeButtonPressed: {
    opacity: 0.85,
  },
  closeButtonLabel: {
    fontFamily: fonts.bold,
    fontSize: 14,
    letterSpacing: 0.5,
    color: colors.paper,
    textTransform: 'uppercase',
  },
});
