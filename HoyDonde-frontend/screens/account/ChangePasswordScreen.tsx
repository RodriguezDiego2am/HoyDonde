import React, { useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import { EmailAuthProvider, reauthenticateWithCredential, updatePassword } from 'firebase/auth';

import { ActionButton } from '@/components/ui/ActionButton';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { Surface } from '@/components/ui/Surface';
import { auth } from '@/config/firebase';
import { colors, fonts, spacing } from '@/constants/theme';
import PasswordFormInput from '@/components/PasswordFormInput';

const MIN_PASSWORD_LENGTH = 6;

function describeChangePasswordError(error: unknown): string {
  const code =
    typeof error === 'object' && error !== null && 'code' in error ? String((error as { code: unknown }).code) : '';

  switch (code) {
    case 'auth/wrong-password':
    case 'auth/invalid-credential':
      return 'La contraseña actual no es correcta.';
    case 'auth/weak-password':
      return 'La contraseña nueva debe tener al menos 6 caracteres.';
    case 'auth/requires-recent-login':
      return 'Tu sesión expiró por seguridad. Volvé a iniciar sesión e intentá de nuevo.';
    case 'auth/network-request-failed':
      return 'No se pudo conectar. Revisá tu conexión a internet.';
    case 'auth/too-many-requests':
      return 'Demasiados intentos. Probá de nuevo en unos minutos.';
    default:
      return 'No pudimos cambiar tu contraseña. Probá de nuevo.';
  }
}

/**
 * Cambio de contraseña autenticado (`/account/security`, universal fuera de tabs): exclusivamente
 * Firebase Client SDK -reautenticación + updatePassword-, nunca pasa por la API. Ningún valor de
 * contraseña se guarda en estado persistente, AsyncStorage, logs ni navegación: los campos viven
 * solo en el estado local de este componente y se limpian apenas se usan.
 */
export default function ChangePasswordScreen() {
  const firebaseUser = auth.currentUser;

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [errors, setErrors] = useState<{ current?: string; next?: string; confirm?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [success, setSuccess] = useState(false);

  const clearSensitiveFields = () => {
    setCurrentPassword('');
    setNewPassword('');
    setConfirmPassword('');
  };

  if (!firebaseUser || !firebaseUser.email) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="MI CUENTA" title="Seguridad" showBack />
        <View style={styles.scroll}>
          <Surface variant="sand">
            <Text style={styles.emptyText}>Tu sesión expiró. Volvé a iniciar sesión para cambiar tu contraseña.</Text>
          </Surface>
          <View style={styles.buttonRow}>
            <ActionButton label="Ir a iniciar sesión" onPress={() => router.replace('/login')} />
          </View>
        </View>
      </View>
    );
  }

  const validate = (): boolean => {
    const newErrors: { current?: string; next?: string; confirm?: string } = {};
    if (!currentPassword) newErrors.current = 'Ingresá tu contraseña actual.';
    if (!newPassword) {
      newErrors.next = 'Ingresá una contraseña nueva.';
    } else if (newPassword.length < MIN_PASSWORD_LENGTH) {
      newErrors.next = `La contraseña nueva debe tener al menos ${MIN_PASSWORD_LENGTH} caracteres.`;
    } else if (currentPassword && newPassword === currentPassword) {
      newErrors.next = 'La contraseña nueva no puede ser igual a la actual.';
    }
    if (!confirmPassword) {
      newErrors.confirm = 'Confirmá la contraseña nueva.';
    } else if (newPassword && confirmPassword !== newPassword) {
      newErrors.confirm = 'Las contraseñas no coinciden.';
    }
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async () => {
    if (submitting) return;
    setFormError(null);
    if (!validate()) return;

    setSubmitting(true);
    try {
      const credential = EmailAuthProvider.credential(firebaseUser.email!, currentPassword);
      await reauthenticateWithCredential(firebaseUser, credential);
      await updatePassword(firebaseUser, newPassword);
      clearSensitiveFields();
      setSuccess(true);
    } catch (error) {
      setFormError(describeChangePasswordError(error));
    } finally {
      setSubmitting(false);
    }
  };

  if (success) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="MI CUENTA" title="Seguridad" showBack />
        <View style={styles.scroll}>
          <Surface variant="sand">
            <Text style={styles.successText}>Tu contraseña se cambió correctamente.</Text>
            <Text style={styles.emptyText}>
              Según cómo lo maneje Firebase, tu sesión actual puede seguir activa o pedirte que vuelvas a iniciar
              sesión.
            </Text>
          </Surface>
          <View style={styles.buttonRow}>
            <ActionButton label="Listo" onPress={() => router.back()} />
          </View>
        </View>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="MI CUENTA" title="Seguridad" subtitle="Actualizá tu contraseña" showBack />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <PasswordFormInput
          label="Contraseña actual"
          value={currentPassword}
          onChangeText={setCurrentPassword}
          placeholder="Tu contraseña actual"
          error={errors.current}
        />
        <PasswordFormInput
          label="Contraseña nueva"
          value={newPassword}
          onChangeText={setNewPassword}
          placeholder={`Al menos ${MIN_PASSWORD_LENGTH} caracteres`}
          error={errors.next}
        />
        <PasswordFormInput
          label="Confirmar contraseña nueva"
          value={confirmPassword}
          onChangeText={setConfirmPassword}
          placeholder="Repetí la contraseña nueva"
          error={errors.confirm}
        />

        {formError ? <Text style={styles.errorText}>{formError}</Text> : null}

        <View style={styles.buttonRow}>
          <ActionButton label="Cambiar contraseña" onPress={handleSubmit} loading={submitting} />
        </View>
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  scroll: {
    padding: spacing.lg,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginTop: spacing.xs,
    marginBottom: spacing.sm,
  },
  successText: {
    fontFamily: fonts.bold,
    fontSize: 16,
    color: colors.success,
  },
  emptyText: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    marginTop: spacing.sm,
  },
  buttonRow: {
    marginTop: spacing.lg,
  },
});
