import React, { useMemo, useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { Surface } from '@/components/ui/Surface';
import { ACCIONES } from '@/constants/acciones';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/apiError';
import { userProvisioningService } from '@/services/userProvisioningService';
import FormInput from '@/components/FormInput';

type TipoAlta = 'ADMINISTRADOR' | 'ORGANIZADOR';

interface FormState {
  email: string;
  password: string;
  confirmPassword: string;
}

type FormErrors = Partial<Record<keyof FormState, string>>;

const INITIAL_FORM: FormState = { email: '', password: '', confirmPassword: '' };

const TIPO_LABEL: Record<TipoAlta, string> = {
  ADMINISTRADOR: 'Administrador',
  ORGANIZADOR: 'Organizador',
};

const TIPO_PLACEHOLDER_EMAIL: Record<TipoAlta, string> = {
  ADMINISTRADOR: 'admin@correo.com',
  ORGANIZADOR: 'organizador@correo.com',
};

function altaErrorMessage(tipo: TipoAlta, error: unknown): string {
  const tipoLabel = TIPO_LABEL[tipo];
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
    if (error.isForbidden) return `Tu cuenta no tiene permiso para crear cuentas de ${tipoLabel}.`;
    return error.message || `No se pudo crear el ${tipoLabel}.`;
  }
  return `No se pudo crear el ${tipoLabel}. Verificá tu conexión.`;
}

/**
 * Alta privilegiada de Administrador/Organizador (API_Documentation.md §6: RegisterAdminDto y
 * RegisterOrganizadorDto solo piden Email/Password). Si el actor tiene ambas acciones habilitadas
 * elige el tipo con un selector; si solo tiene una, el formulario va directo a esa sin mostrar
 * un selector inútil. Crear Control sigue siendo responsabilidad exclusiva del Organizador
 * (app/organizer), nunca de esta pantalla.
 */
export default function AltaPrivilegiadaScreen() {
  const { hasAccion } = useAuth();
  const puedeAdmin = hasAccion(ACCIONES.USUARIO_CREAR_ADMIN);
  const puedeOrganizador = hasAccion(ACCIONES.USUARIO_CREAR_ORGANIZADOR);

  const tiposDisponibles = useMemo<TipoAlta[]>(() => {
    const tipos: TipoAlta[] = [];
    if (puedeAdmin) tipos.push('ADMINISTRADOR');
    if (puedeOrganizador) tipos.push('ORGANIZADOR');
    return tipos;
  }, [puedeAdmin, puedeOrganizador]);

  const [tipo, setTipo] = useState<TipoAlta | null>(tiposDisponibles[0] ?? null);
  const [formData, setFormData] = useState<FormState>(INITIAL_FORM);
  const [errors, setErrors] = useState<FormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [created, setCreated] = useState<{ tipo: TipoAlta; email: string } | null>(null);

  if (tiposDisponibles.length === 0) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Altas" showBack />
        <View style={styles.blockedWrap}>
          <MaterialIcons name="lock" size={28} color={colors.inkSoft} />
          <Text style={styles.blockedText}>Tu cuenta no tiene acciones habilitadas para dar altas.</Text>
        </View>
      </View>
    );
  }

  const tipoActivo = tipo ?? tiposDisponibles[0];

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
      if (tipoActivo === 'ADMINISTRADOR') {
        await userProvisioningService.registerAdmin({ email: formData.email, password: formData.password });
      } else {
        await userProvisioningService.registerOrganizador({ email: formData.email, password: formData.password });
      }
      setCreated({ tipo: tipoActivo, email: formData.email });
    } catch (error) {
      setFormError(altaErrorMessage(tipoActivo, error));
    } finally {
      setSubmitting(false);
    }
  };

  const resetForm = () => {
    setFormData(INITIAL_FORM);
    setErrors({});
    setFormError(null);
    setCreated(null);
  };

  if (created) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ADMINISTRACIÓN" title={`${TIPO_LABEL[created.tipo]} creado`} showBack />
        <ScrollView contentContainerStyle={styles.scroll}>
          <Surface style={styles.successCard}>
            <MaterialIcons name="check-circle" size={32} color={colors.success} />
            <Text style={styles.successTitle}>{TIPO_LABEL[created.tipo]} creado exitosamente.</Text>
            <Text style={styles.successHint}>{created.email}</Text>
          </Surface>

          <View style={styles.buttonSpacing}>
            <ActionButton label="Crear otra cuenta" onPress={resetForm} />
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
        title="Nueva cuenta"
        subtitle="Alta privilegiada: solo se autoriza desde acá, nunca por autoregistro."
        showBack
      />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        {tiposDisponibles.length > 1 ? (
          <>
            <SectionDivider index="01" label="Tipo de cuenta" style={styles.firstDivider} />
            <View style={styles.tipoRow}>
              {tiposDisponibles.map((opcion) => {
                const active = tipoActivo === opcion;
                return (
                  <View key={opcion} style={styles.tipoButtonWrap}>
                    <ActionButton
                      label={TIPO_LABEL[opcion]}
                      variant={active ? 'primary' : 'secondary'}
                      onPress={() => setTipo(opcion)}
                    />
                  </View>
                );
              })}
            </View>
          </>
        ) : null}

        <SectionDivider
          index={tiposDisponibles.length > 1 ? '02' : '01'}
          label="Acceso"
          style={tiposDisponibles.length > 1 ? undefined : styles.firstDivider}
        />

        <FormInput
          label="Email"
          value={formData.email}
          onChangeText={(text) => handleChange('email', text)}
          placeholder={TIPO_PLACEHOLDER_EMAIL[tipoActivo]}
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
          <ActionButton
            label={`Crear ${TIPO_LABEL[tipoActivo]}`}
            onPress={handleSubmit}
            loading={submitting}
          />
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
  tipoRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  // flex:1 en cada botón en vez de un ancho fijo: reparten el ancho disponible en partes
  // iguales y nunca se desbordan, sin importar el tamaño del dispositivo.
  tipoButtonWrap: {
    flex: 1,
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
  blockedWrap: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    padding: spacing.lg,
  },
  blockedText: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.inkSoft,
    textAlign: 'center',
  },
});
