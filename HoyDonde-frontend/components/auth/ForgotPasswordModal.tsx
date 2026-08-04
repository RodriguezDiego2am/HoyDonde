import React, { useState } from 'react';
import { Modal, StyleSheet, Text, View } from 'react-native';
import { sendPasswordResetEmail } from 'firebase/auth';

import { ActionButton } from '@/components/ui/ActionButton';
import { auth } from '@/config/firebase';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { isAccountEnumerationError } from '@/utils/firebaseAuthErrors';
import { isValidEmailFormat } from '@/utils/emailValidation';
import FormInput from '../FormInput';

const PRUDENT_MESSAGE = 'Si existe una cuenta asociada, recibirás instrucciones para restablecerla.';
const CONTROL_NOTICE =
  'Si ingresás como Control y olvidaste tu contraseña, solicitá al Administrador un enlace de recuperación.';

interface ForgotPasswordModalProps {
  visible: boolean;
  onClose: () => void;
}

/** "Olvidé mi contraseña" (API_Documentation.md / recuperación pública): Firebase Client SDK
 * exclusivamente -nunca pasa por la API-, mensaje siempre prudente, sin revelar si el email
 * existe. */
export function ForgotPasswordModal({ visible, onClose }: ForgotPasswordModalProps) {
  const [email, setEmail] = useState('');
  const [emailError, setEmailError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);

  const reset = () => {
    setEmail('');
    setEmailError(null);
    setFormError(null);
    setSending(false);
    setSent(false);
  };

  const handleClose = () => {
    reset();
    onClose();
  };

  const handleSubmit = async () => {
    if (sending) return;

    const trimmed = email.trim();
    if (!isValidEmailFormat(trimmed)) {
      setEmailError('Ingresá un email válido.');
      return;
    }
    setEmailError(null);
    setFormError(null);
    setSending(true);

    try {
      await sendPasswordResetEmail(auth, trimmed);
      setSent(true);
    } catch (error) {
      if (isAccountEnumerationError(error)) {
        // Nunca revelar si el email existe: mismo resultado prudente que un envío exitoso.
        setSent(true);
      } else {
        setFormError('No pudimos procesar tu pedido. Probá de nuevo.');
      }
    } finally {
      setSending(false);
    }
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={handleClose}>
      <View style={styles.scrim}>
        <View style={styles.card}>
          <Text style={styles.title}>Recuperar contraseña</Text>

          {sent ? (
            <>
              <Text style={styles.message}>{PRUDENT_MESSAGE}</Text>
              <View style={styles.buttonRow}>
                <ActionButton label="Cerrar" onPress={handleClose} />
              </View>
            </>
          ) : (
            <>
              <Text style={styles.message}>Ingresá tu email y te enviaremos instrucciones para restablecerla.</Text>

              <View style={styles.field}>
                <FormInput
                  label="Email"
                  value={email}
                  onChangeText={(text) => {
                    setEmail(text);
                    if (emailError) setEmailError(null);
                  }}
                  placeholder="ejemplo@correo.com"
                  keyboardType="email-address"
                  error={emailError}
                />
              </View>

              {formError ? <Text style={styles.errorText}>{formError}</Text> : null}

              <Text style={styles.controlNotice}>{CONTROL_NOTICE}</Text>

              <View style={styles.actions}>
                <View style={styles.actionButton}>
                  <ActionButton label="Cancelar" variant="ghost" onPress={handleClose} disabled={sending} />
                </View>
                <View style={styles.actionButton}>
                  <ActionButton label="Enviar instrucciones" onPress={handleSubmit} loading={sending} />
                </View>
              </View>
            </>
          )}
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
  field: {
    marginTop: spacing.md,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginTop: spacing.xs,
  },
  controlNotice: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    marginTop: spacing.md,
    lineHeight: 17,
  },
  actions: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.lg,
  },
  actionButton: {
    flex: 1,
  },
  buttonRow: {
    marginTop: spacing.lg,
  },
});
