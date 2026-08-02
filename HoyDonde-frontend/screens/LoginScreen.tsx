import React, { useState } from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AuthShell } from '@/components/ui/AuthShell';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { describeFirebaseAuthError } from '@/utils/firebaseAuthErrors';
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

  const validate = () => {
    const newErrors: { email?: string; password?: string } = {};
    if (!email) newErrors.email = 'El email es obligatorio';
    if (!password) newErrors.password = 'La contraseña es obligatoria';
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleLogin = async () => {
    setFormError(null);
    if (!validate()) return;

    setLoading(true);
    try {
      await loginWithEmail(email, password);
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
        label="Email"
        value={email}
        onChangeText={setEmail}
        placeholder="ejemplo@correo.com"
        error={errors.email}
        keyboardType="email-address"
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
    </AuthShell>
  );
}

const styles = StyleSheet.create({
  formError: {
    fontFamily: fonts.medium,
    color: colors.error,
    marginBottom: spacing.md,
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
