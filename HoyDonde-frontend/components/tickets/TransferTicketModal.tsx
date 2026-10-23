import React, { useEffect, useRef, useState } from 'react';
import { Modal, ScrollView, StyleSheet, Text, View } from 'react-native';

import FormInput from '@/components/FormInput';
import { ActionButton } from '@/components/ui/ActionButton';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { ApiError, ticketService } from '@/services/APIService';

interface TransferTicketModalProps {
  visible: boolean;
  ticketId: string;
  eventoNombre: string;
  ticketTypeNombre: string;
  onClose: () => void;
  /** Se llama una sola vez cuando la API confirma la transferencia (el ticket ya no es del usuario). */
  onTransferred: (ticketId: string) => void;
}

type Step = 'form' | 'confirm' | 'success';

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Mensajes públicos por código de error de POST /api/tickets/{id}/transfer. Nunca incluyen el email ingresado. */
export function transferErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'TRANSFER_RECIPIENT_INVALID':
        return 'No pudimos transferir la entrada a ese destinatario. Verificá que tenga una cuenta activa en HoyDonde.';
      case 'TRANSFER_TO_SELF':
        return 'No podés transferirte una entrada a vos mismo.';
      case 'TICKET_NOT_TRANSFERABLE':
        return 'Esta entrada ya no se puede transferir (usada, anulada o el evento ya no está vigente).';
      case 'TICKET_NOT_FOUND':
        return 'No encontramos esta entrada entre las tuyas. Actualizá Mis entradas.';
      case 'TRANSFER_RATE_LIMITED':
        return 'Alcanzaste el límite de 10 transferencias por hora. Intentá de nuevo más tarde.';
      default:
        break;
    }
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para transferir entradas.';
    return error.message || 'No se pudo transferir la entrada.';
  }
  return 'No se pudo transferir la entrada. Verificá tu conexión.';
}

/**
 * Transferencia gratuita e inmediata de una entrada a otro usuario registrado, por email.
 * Paso 1: email. Paso 2: confirmación explícita (inmediata e irreversible). Paso 3: resultado.
 * La API decide todo (dueño, estado, evento vigente, destinatario válido, límite por hora); acá
 * solo se valida el formato del email y se evita el doble envío.
 */
export function TransferTicketModal({
  visible,
  ticketId,
  eventoNombre,
  ticketTypeNombre,
  onClose,
  onTransferred,
}: TransferTicketModalProps) {
  const [step, setStep] = useState<Step>('form');
  const [email, setEmail] = useState('');
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);

  useEffect(() => {
    if (visible) {
      setStep('form');
      setEmail('');
      setFieldError(null);
      setSubmitError(null);
      setSubmitting(false);
      submittingRef.current = false;
    }
  }, [visible, ticketId]);

  const trimmedEmail = email.trim();

  const handleContinue = () => {
    if (!EMAIL_PATTERN.test(trimmedEmail)) {
      setFieldError('Ingresá un email válido.');
      return;
    }
    setFieldError(null);
    setSubmitError(null);
    setStep('confirm');
  };

  const handleConfirm = async () => {
    if (submittingRef.current) return;
    submittingRef.current = true;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await ticketService.transfer(ticketId, trimmedEmail);
      setStep('success');
      onTransferred(ticketId);
    } catch (error) {
      setSubmitError(transferErrorMessage(error));
      setStep('form');
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={submitting ? () => {} : onClose}>
      <View style={styles.scrim}>
        <View style={styles.card} accessibilityViewIsModal>
          <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.cardContent}>
            <Text style={styles.eyebrow}>TRANSFERIR ENTRADA</Text>
            <Text style={styles.eventName} numberOfLines={2}>
              {eventoNombre}
            </Text>
            <Text style={styles.ticketType}>{ticketTypeNombre}</Text>

            {step === 'form' ? (
              <>
                <Text style={styles.body}>
                  Pasale esta entrada a otra persona que ya tenga cuenta en HoyDonde?. Es gratis y no necesita que la
                  acepte.
                </Text>
                <View style={styles.formBlock}>
                  <FormInput
                    label="Email del destinatario"
                    value={email}
                    onChangeText={(text) => {
                      setEmail(text);
                      if (fieldError) setFieldError(null);
                    }}
                    placeholder="nombre@ejemplo.com"
                    keyboardType="email-address"
                    autoCapitalize="none"
                    error={fieldError}
                  />
                </View>
                {submitError ? (
                  <Text accessibilityRole="alert" accessibilityLiveRegion="polite" style={styles.errorText}>
                    {submitError}
                  </Text>
                ) : null}
                <View style={styles.actions}>
                  <View style={styles.actionCell}>
                    <ActionButton label="Cancelar" variant="ghost" onPress={onClose} />
                  </View>
                  <View style={styles.actionCell}>
                    <ActionButton label="Continuar" onPress={handleContinue} disabled={trimmedEmail.length === 0} />
                  </View>
                </View>
              </>
            ) : null}

            {step === 'confirm' ? (
              <>
                <Text style={styles.confirmTitle}>¿Transferir esta entrada?</Text>
                <Text style={styles.body}>
                  Vas a transferirla a <Text style={styles.bold}>{trimmedEmail}</Text>.
                </Text>
                <Text style={styles.warning}>
                  La transferencia es inmediata e irreversible: la entrada deja de ser tuya, tu QR actual deja de valer y
                  solo la otra persona podrá usarla o volver a transferirla.
                </Text>
                <View style={styles.actions}>
                  <View style={styles.actionCell}>
                    <ActionButton label="Volver" variant="ghost" onPress={() => setStep('form')} disabled={submitting} />
                  </View>
                  <View style={styles.actionCell}>
                    <ActionButton label="Transferir ahora" variant="danger" onPress={handleConfirm} loading={submitting} />
                  </View>
                </View>
              </>
            ) : null}

            {step === 'success' ? (
              <>
                <Text accessibilityRole="alert" accessibilityLiveRegion="polite" style={styles.successTitle}>
                  Entrada transferida
                </Text>
                <Text style={styles.body}>
                  La entrada ya está en la cuenta de {trimmedEmail}. Tu QR anterior dejó de valer.
                </Text>
                <View style={styles.actionsSingle}>
                  <ActionButton label="Listo" onPress={onClose} />
                </View>
              </>
            ) : null}
          </ScrollView>
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
    maxWidth: 380,
    maxHeight: '90%',
    backgroundColor: colors.paper,
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderRadius: radii.md,
  },
  cardContent: {
    padding: spacing.lg,
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
    marginTop: spacing.xs,
  },
  ticketType: {
    fontFamily: fonts.semiBold,
    fontSize: 12,
    color: colors.inkSoft,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginTop: 2,
  },
  body: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.ink,
    lineHeight: 20,
    marginTop: spacing.md,
  },
  bold: {
    fontFamily: fonts.bold,
  },
  formBlock: {
    marginTop: spacing.md,
  },
  confirmTitle: {
    fontFamily: fonts.bold,
    fontSize: 17,
    color: colors.ink,
    marginTop: spacing.lg,
  },
  warning: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    lineHeight: 19,
    marginTop: spacing.md,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginTop: spacing.xs,
  },
  successTitle: {
    fontFamily: fonts.black,
    fontSize: 20,
    color: colors.ink,
    marginTop: spacing.lg,
  },
  actions: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.lg,
  },
  actionCell: {
    flex: 1,
  },
  actionsSingle: {
    marginTop: spacing.lg,
  },
});
