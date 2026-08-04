import React, { useState } from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AuthShell } from '@/components/ui/AuthShell';
import { ForgotPasswordModal } from '@/components/auth/ForgotPasswordModal';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { describeFirebaseAuthError } from '@/utils/firebaseAuthErrors';
import { resolveLoginEmail } from '@/utils/controlLoginEmail';
import { isSafeReturnTo } from '@/utils/navigation';
import FormInput from '../components/FormInput';

export default function LoginScreen() {
  const { loginWithEmail } = useAuth();
  const { returnTo } = useLocalSearchParams<{ returnTo?: string }>();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [forgotPasswordVisible, setForgotPasswordVisible] = useState(false);

  const validate = () => {
    const newErrors: { email?: string; password?: string } = {};
    if (!email.trim()) newErrors.email = 'El email o usuario es obligatorio';
    if (!password) newErrors.password = 'La contraseña es obligatoria';
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleLogin = async () => {
    setFormError(null);
    if (!validate()) return;

    setLoading(true);
    try {
      // Nunca se muestra ni se loguea el email sintético resuelto: Firebase lo recibe
      // internamente, pero la UI solo conoció el identificador que tipeó el Control.
      await loginWithEmail(resolveLoginEmail(email), password);
      // returnTo es un valor dinámico validado en runtime (isSafeReturnTo), no una
      // ruta literal conocida en build-time: expo-router v6 exige el cast a Href
      // para navegación dinámica bajo typed routes.
      router.replace((isSafeReturnTo(returnTo) ? returnTo : '/(tabs)') as Href);
    } catch (error) {
      setFormError(describeFirebaseAuthError(error));
    } finally {
      setLoading(false);
    }
  };

  return (
    <AuthShell title="Iniciar sesión" subtitle="Entrá con tu email y contraseña.">
      <FormInput
        label="Email o usuario"
        value={email}
        onChangeText={setEmail}
        placeholder="ejemplo@correo.com o tu usuario"
        error={errors.email}
        keyboardType="default"
      />

      <FormInput
        label="Contraseña"
        value={password}
        onChangeText={setPassword}
        placeholder="Tu contraseña"
        secureTextEntry
        error={errors.password}
      />

      {formError ? <Text style={styles.formError}>{formError}</Text> : null}

      <View style={styles.forgotPasswordRow}>
        <TouchableOpacity onPress={() => setForgotPasswordVisible(true)}>
          <Text style={styles.forgotPasswordLink}>¿Olvidaste tu contraseña?</Text>
        </TouchableOpacity>
      </View>

      <View style={styles.buttonContainer}>
        <ActionButton label="Ingresar" onPress={handleLogin} loading={loading} />
      </View>

      <View style={styles.registerContainer}>
        <Text style={styles.registerText}>¿No tenés cuenta? </Text>
        <TouchableOpacity style={styles.registerLinkRow} onPress={() => router.push('/register')}>
          <Text style={styles.registerLink}>Registrate</Text>
          <MaterialIcons name="north-east" size={14} color={colors.cobalt} />
        </TouchableOpacity>
      </View>

      <ForgotPasswordModal visible={forgotPasswordVisible} onClose={() => setForgotPasswordVisible(false)} />
    </AuthShell>
  );
}

const styles = StyleSheet.create({
  formError: {
    fontFamily: fonts.medium,
    color: colors.error,
    marginBottom: spacing.md,
  },
  forgotPasswordRow: {
    alignItems: 'flex-end',
    marginTop: spacing.xs,
  },
  forgotPasswordLink: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.cobalt,
    textDecorationLine: 'underline',
  },
  buttonContainer: {
    marginTop: spacing.sm,
  },
  registerContainer: {
    flexDirection: 'row',
    justifyContent: 'center',
    marginTop: spacing.xl,
  },
  registerText: {
    fontFamily: fonts.regular,
    color: colors.ink,
  },
  registerLinkRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
  },
  registerLink: {
    fontFamily: fonts.bold,
    color: colors.cobalt,
    textDecorationLine: 'underline',
  },
});
