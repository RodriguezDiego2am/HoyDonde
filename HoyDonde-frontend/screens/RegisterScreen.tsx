import React, { useState } from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { router } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AuthShell } from '@/components/ui/AuthShell';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { describeFirebaseAuthError } from '@/utils/firebaseAuthErrors';
import FormInput from '../components/FormInput';

interface FormState {
  email: string;
  password: string;
  confirmPassword: string;
  fullName: string;
  dni: string;
  phoneNumber: string;
}

type FormErrors = Partial<Record<keyof FormState, string>>;

const INITIAL_FORM: FormState = {
  email: '',
  password: '',
  confirmPassword: '',
  fullName: '',
  dni: '',
  phoneNumber: '',
};

export default function RegisterScreen() {
  const { registerCliente } = useAuth();
  const [formData, setFormData] = useState<FormState>(INITIAL_FORM);
  const [errors, setErrors] = useState<FormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

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

    if (!formData.fullName) {
      newErrors.fullName = 'El nombre completo es obligatorio';
    }

    if (!formData.dni) {
      newErrors.dni = 'El DNI es obligatorio';
    }

    if (!formData.phoneNumber) {
      newErrors.phoneNumber = 'El número de teléfono es obligatorio';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleChange = (name: keyof FormState, value: string) => {
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const handleRegister = async () => {
    setFormError(null);
    if (!validate()) return;

    setLoading(true);
    try {
      await registerCliente({
        email: formData.email,
        password: formData.password,
        fullName: formData.fullName,
        dni: formData.dni,
        phoneNumber: formData.phoneNumber,
      });
      // La contraseña solo vivió en este estado local; se descarta al navegar.
      router.replace('/(tabs)');
    } catch (error) {
      setFormError(describeFirebaseAuthError(error));
    } finally {
      setLoading(false);
    }
  };

  return (
    <AuthShell title="Crear cuenta" subtitle="Registrate como Cliente para comprar entradas.">
      <SectionDivider index="01" label="Acceso" style={styles.firstDivider} />

      <FormInput
        label="Email"
        value={formData.email}
        onChangeText={(text) => handleChange('email', text)}
        placeholder="ejemplo@correo.com"
        error={errors.email}
        keyboardType="email-address"
      />

      <FormInput
        label="Contraseña"
        value={formData.password}
        onChangeText={(text) => handleChange('password', text)}
        placeholder="Tu contraseña"
        secureTextEntry
        error={errors.password}
      />

      <FormInput
        label="Confirmar contraseña"
        value={formData.confirmPassword}
        onChangeText={(text) => handleChange('confirmPassword', text)}
        placeholder="Confirmá tu contraseña"
        secureTextEntry
        error={errors.confirmPassword}
      />

      <SectionDivider index="02" label="Tus datos" />

      <FormInput
        label="Nombre completo"
        value={formData.fullName}
        onChangeText={(text) => handleChange('fullName', text)}
        placeholder="Tu nombre completo"
        error={errors.fullName}
        autoCapitalize="words"
      />

      <FormInput
        label="DNI"
        value={formData.dni}
        onChangeText={(text) => handleChange('dni', text)}
        placeholder="Tu DNI"
        error={errors.dni}
        keyboardType="numeric"
      />

      <FormInput
        label="Número de teléfono"
        value={formData.phoneNumber}
        onChangeText={(text) => handleChange('phoneNumber', text)}
        placeholder="Tu número de teléfono"
        error={errors.phoneNumber}
        keyboardType="phone-pad"
      />

      {formError ? <Text style={styles.formError}>{formError}</Text> : null}

      <View style={styles.buttonContainer}>
        <ActionButton label="Registrarse" onPress={handleRegister} loading={loading} />
      </View>

      <TouchableOpacity style={styles.loginLink} onPress={() => router.push('/login')}>
        <Text style={styles.loginLinkText}>¿Ya tenés cuenta? Iniciar sesión</Text>
        <MaterialIcons name="north-east" size={14} color={colors.cobalt} />
      </TouchableOpacity>
    </AuthShell>
  );
}

const styles = StyleSheet.create({
  firstDivider: {
    marginTop: 0,
  },
  formError: {
    fontFamily: fonts.medium,
    color: colors.error,
    marginBottom: spacing.md,
  },
  buttonContainer: {
    marginTop: spacing.sm,
  },
  loginLink: {
    marginTop: spacing.xl,
    marginBottom: spacing.lg,
    flexDirection: 'row',
    justifyContent: 'center',
    alignItems: 'center',
    gap: 4,
  },
  loginLinkText: {
    fontFamily: fonts.bold,
    color: colors.cobalt,
    fontSize: 15,
    textDecorationLine: 'underline',
  },
});
