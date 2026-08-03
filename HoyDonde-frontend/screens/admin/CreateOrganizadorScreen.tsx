import React, { useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
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
  email: string;
  password: string;
  confirmPassword: string;
}

type FormErrors = Partial<Record<keyof FormState, string>>;

const INITIAL_FORM: FormState = { email: '', password: '', confirmPassword: '' };

function registerOrganizadorErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'IDENTITY_EMAIL_ALREADY_EXISTS':
        return 'Ya existe una cuenta con este email.';
      case 'VALIDATION_ERROR': {
        const first = error.errors ? Object.values(error.errors).flat()[0] : undefined;
        return first ?? error.message;
      }
      default:
        break;
    }
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para crear Organizadores.';
    return error.message || 'No se pudo crear el Organizador.';
  }
  return 'No se pudo crear el Organizador. Verificá tu conexión.';
}

/** Alta mínima de Organizador (API_Documentation.md §6): solo Email/Password, los únicos campos que exige RegisterOrganizadorDto. */
export default function CreateOrganizadorScreen() {
  const [formData, setFormData] = useState<FormState>(INITIAL_FORM);
  const [errors, setErrors] = useState<FormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [createdEmail, setCreatedEmail] = useState<string | null>(null);

  const validate = (): boolean => {
    const newErrors: FormErrors = {};

    if (!formData.email) {
      newErrors.email = 'El email es obligatorio';
    } else if (!/\S+@\S+\.\S+/.test(formData.email)) {
      newErrors.email = 'Email inválido';
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
    if (submitting) return;
    if (!validate()) return;

    setSubmitting(true);
    try {
      await userProvisioningService.registerOrganizador({
        email: formData.email,
        password: formData.password,
      });
      setCreatedEmail(formData.email);
    } catch (error) {
      setFormError(registerOrganizadorErrorMessage(error));
    } finally {
      setSubmitting(false);
    }
  };

  const resetForm = () => {
    setFormData(INITIAL_FORM);
    setErrors({});
    setFormError(null);
    setCreatedEmail(null);
  };

  if (createdEmail) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Organizador creado" showBack />
        <ScrollView contentContainerStyle={styles.scroll}>
          <Surface style={styles.successCard}>
            <MaterialIcons name="check-circle" size={32} color={colors.success} />
            <Text style={styles.successTitle}>Organizador creado exitosamente.</Text>
            <Text style={styles.successHint}>{createdEmail}</Text>
          </Surface>

          <View style={styles.buttonSpacing}>
            <ActionButton label="Crear otro Organizador" onPress={resetForm} />
          </View>
          <View style={styles.buttonSpacing}>
            <ActionButton label="Volver" variant="secondary" onPress={() => router.back()} />
          </View>
        </ScrollView>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <EditorialHeader
        eyebrow="ADMINISTRACIÓN"
        title="Nuevo Organizador"
        subtitle="Solo un Administrador puede dar de alta esta cuenta."
        showBack
      />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <SectionDivider index="01" label="Acceso" style={styles.firstDivider} />

        <FormInput
          label="Email"
          value={formData.email}
          onChangeText={(text) => handleChange('email', text)}
          placeholder="organizador@correo.com"
          error={errors.email}
          keyboardType="email-address"
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
          <ActionButton label="Crear Organizador" onPress={handleSubmit} loading={submitting} />
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
  },
});
