import React, { useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import { ApiError } from '@/services/apiError';
import { userProvisioningService } from '@/services/userProvisioningService';
import FormInput from '@/components/FormInput';

interface FormState {
  userName: string;
  password: string;
  confirmPassword: string;
}

type FormErrors = Partial<Record<keyof FormState, string>>;

const INITIAL_FORM: FormState = { userName: '', password: '', confirmPassword: '' };

function registerControlErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'IDENTITY_EMAIL_ALREADY_EXISTS':
        return 'Ya existe una cuenta con ese nombre de usuario.';
      case 'EVENT_NOT_FOUND':
        return 'El evento ya no existe.';
      case 'VALIDATION_ERROR': {
        const first = error.errors ? Object.values(error.errors).flat()[0] : undefined;
        return first ?? error.message;
      }
      default:
        break;
    }
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'No tenés permiso para crear un Control para este evento.';
    return error.message || 'No se pudo crear el Control.';
  }
  return 'No se pudo crear el Control. Verificá tu conexión.';
}

/** Alta mínima de Control (API_Documentation.md §6): userName/password, asociado siempre a `eventId` (route param), nunca editable a mano. */
export default function CreateControlScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();

  const [formData, setFormData] = useState<FormState>(INITIAL_FORM);
  const [errors, setErrors] = useState<FormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [createdUserName, setCreatedUserName] = useState<string | null>(null);

  const validate = (): boolean => {
    const newErrors: FormErrors = {};

    if (!formData.userName.trim()) {
      newErrors.userName = 'El nombre de usuario es obligatorio';
    }

    if (!formData.password) {
      newErrors.password = 'La contraseña es obligatoria';
    } else if (formData.password.length < 6) {
      newErrors.password = 'La contraseña debe tener al menos 6 caracteres';
    }

    if (formData.password !== formData.confirmPassword) {
      newErrors.confirmPassword = 'Las contraseñas no coinciden';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleChange = (name: keyof FormState, value: string) => {
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const handleSubmit = async () => {
    setFormError(null);
    if (submitting || !id) return;
    if (!validate()) return;

    setSubmitting(true);
    try {
      await userProvisioningService.registerControl({
        userName: formData.userName.trim(),
        password: formData.password,
        eventId: id,
      });
      setCreatedUserName(formData.userName.trim());
    } catch (error) {
      setFormError(registerControlErrorMessage(error));
    } finally {
      setSubmitting(false);
    }
  };

  const resetForm = () => {
    setFormData(INITIAL_FORM);
    setErrors({});
    setFormError(null);
    setCreatedUserName(null);
  };

  if (createdUserName) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ORGANIZACIÓN" title="Control creado" showBack />
        <ScrollView contentContainerStyle={styles.scroll}>
          <Surface style={styles.successCard}>
            <MaterialIcons name="check-circle" size={32} color={colors.success} />
            <Text style={styles.successTitle}>Control creado exitosamente.</Text>
            <Text style={styles.successHint}>Iniciá sesión con el usuario:</Text>
            <Text style={styles.successUserName}>{createdUserName}</Text>
          </Surface>

          <View style={styles.buttonSpacing}>
            <ActionButton label="Crear otro Control" onPress={resetForm} />
          </View>
          <View style={styles.buttonSpacing}>
            <ActionButton label="Volver al evento" variant="secondary" onPress={() => router.back()} />
          </View>
        </ScrollView>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <EditorialHeader
        eyebrow="ORGANIZACIÓN"
        title="Nuevo Control"
        subtitle="La cuenta queda asociada a este evento."
        showBack
      />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <SectionDivider index="01" label="Acceso" style={styles.firstDivider} />

        <FormInput
          label="Nombre de usuario"
          value={formData.userName}
          onChangeText={(text) => handleChange('userName', text)}
          placeholder="control_puerta_norte"
          error={errors.userName}
        />
        <FormInput
          label="Contraseña"
          value={formData.password}
          onChangeText={(text) => handleChange('password', text)}
          placeholder="Contraseña temporal"
          secureTextEntry
          error={errors.password}
        />
        <FormInput
          label="Confirmar contraseña"
          value={formData.confirmPassword}
          onChangeText={(text) => handleChange('confirmPassword', text)}
          placeholder="Repetí la contraseña"
          secureTextEntry
          error={errors.confirmPassword}
        />

        {formError ? <Text style={styles.formError}>{formError}</Text> : null}

        <View style={styles.buttonSpacing}>
          <ActionButton label="Crear Control" onPress={handleSubmit} loading={submitting} />
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
  firstDivider: {
    marginTop: 0,
  },
  formError: {
    fontFamily: fonts.medium,
    color: colors.error,
    marginTop: spacing.xs,
    marginBottom: spacing.sm,
  },
  buttonSpacing: {
    marginTop: spacing.md,
  },
  successCard: {
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.xl,
  },
  successTitle: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.ink,
    textAlign: 'center',
  },
  successHint: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    textAlign: 'center',
    marginTop: spacing.sm,
  },
  successUserName: {
    fontFamily: fonts.bold,
    fontSize: 16,
    color: colors.ink,
    textAlign: 'center',
  },
});
