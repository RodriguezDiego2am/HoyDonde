import React from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';

import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { buildTicketQrValue } from '@/utils/ticketQr';

interface TicketQRModalProps {
  visible: boolean;
  /** Token opaco emitido por el backend; null si el ticket no tiene un QR utilizable. */
  qrToken: string | null | undefined;
  eventoNombre: string;
  onClose: () => void;
}

/**
 * Dibuja el token opaco que emitió el backend (utils/ticketQr.ts). No contiene ids ni datos del
 * ticket: la API lo resuelve al validar y lo rota si la entrada se transfiere, por lo que un QR
 * viejo (o una captura de pantalla) deja de servir. El mismo valor se muestra como texto
 * seleccionable, como fallback operativo si el QR no se puede escanear.
 */
export function TicketQRModal({ visible, qrToken, eventoNombre, onClose }: TicketQRModalProps) {
  const qrValue = buildTicketQrValue(qrToken);

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <View style={styles.scrim}>
        <View style={styles.card}>
          <Text style={styles.eyebrow}>ENTRADA DIGITAL</Text>
          <Text style={styles.eventName} numberOfLines={2}>
            {eventoNombre}
          </Text>

          {qrValue ? (
            <>
              <View style={styles.qrFrame}>
                <QRCode value={qrValue} size={200} color={colors.ink} backgroundColor={colors.paper} />
              </View>

              <Text style={styles.hint}>
                Mostrá este código al Control. Cambia si la entrada se transfiere: un QR anterior deja de valer.
              </Text>

              <View style={styles.idsBlock}>
                <View style={styles.idRow}>
                  <Text style={styles.idLabel}>CÓDIGO</Text>
                  <Text selectable style={styles.idValue}>
                    {qrValue}
                  </Text>
                </View>
              </View>
              <Text style={styles.copyHint}>Mantené presionado el código para copiarlo.</Text>
            </>
          ) : (
            <Text accessibilityRole="alert" style={styles.hint}>
              Esta entrada no tiene un QR disponible. Actualizá Mis entradas e intentá de nuevo.
            </Text>
          )}

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
