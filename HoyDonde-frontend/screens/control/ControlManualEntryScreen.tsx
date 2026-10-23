import React, { useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';

import { ControlResultView } from '@/components/control/ControlResultView';
import FormInput from '@/components/FormInput';
import { ActionButton } from '@/components/ui/ActionButton';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { colors, fonts, spacing } from '@/constants/theme';
import { useTicketValidation } from '@/hooks/useTicketValidation';
import { parseTicketQrToken } from '@/utils/ticketQr';

/**
 * Alternativa secundaria al escáner (Frontend 3): comparte servicio, lock y presentación de
 * resultado con ControlScanScreen a través de useTicketValidation/ControlResultView — nunca
 * duplica la lógica de validación. El código puede pegarse desde el fallback legible de "Mis
 * entradas" (components/ui/TicketQRModal.tsx): es el mismo token opaco que codifica el QR.
 */
export default function ControlManualEntryScreen() {
  const [code, setCode] = useState('');
  const { result, validating, validate, reset } = useTicketValidation();

  const qrToken = parseTicketQrToken(code);
  const showFormatError = code.trim().length > 0 && qrToken === null;
  const canSubmit = qrToken !== null && !validating;

  const handleSubmit = () => {
    if (!qrToken || validating) return;
    void validate(qrToken);
  };

  const handleScanNext = () => {
    setCode('');
    reset();
  };

  if (result) {
    return <ControlResultView result={result} onScanNext={handleScanNext} nextActionLabel="Validar otra entrada" />;
  }

  return (
    <View style={styles.container}>
      <EditorialHeader
        eyebrow="CONTROL"
        title="Ingreso manual"
        subtitle="Alternativa al escaneo, para cuando el QR no se puede leer."
        showBack
      />

      <ScrollView contentContainerStyle={styles.form} keyboardShouldPersistTaps="handled">
        <FormInput
          label="Código de la entrada"
          value={code}
          onChangeText={setCode}
          placeholder="22 caracteres"
          error={showFormatError ? 'El código debe tener 22 caracteres (letras, números, - o _).' : null}
        />

        <Text style={styles.hint}>
          Pedile al cliente el código que aparece debajo del QR en &quot;Mis entradas&quot;. Es el mismo que codifica el QR
          y cambia si la entrada se transfiere.
        </Text>

        <View style={styles.buttonRow}>
          <ActionButton label="Validar entrada" onPress={handleSubmit} loading={validating} disabled={!canSubmit} />
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
  form: {
    padding: spacing.lg,
  },
  hint: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    marginTop: spacing.xs,
  },
  buttonRow: {
    marginTop: spacing.lg,
  },
});
