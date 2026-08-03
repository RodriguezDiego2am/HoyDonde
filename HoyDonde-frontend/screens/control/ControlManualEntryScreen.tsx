import React, { useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';

import { ControlResultView } from '@/components/control/ControlResultView';
import FormInput from '@/components/FormInput';
import { ActionButton } from '@/components/ui/ActionButton';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { colors, fonts, spacing } from '@/constants/theme';
import { useTicketValidation } from '@/hooks/useTicketValidation';

/**
 * Alternativa secundaria al escáner (Frontend 3): comparte servicio, lock y presentación de
 * resultado con ControlScanScreen a través de useTicketValidation/ControlResultView — nunca
 * duplica la lógica de validación. Los ids pueden pegarse desde el fallback legible de "Mis
 * entradas" (components/ui/TicketQRModal.tsx).
 */
export default function ControlManualEntryScreen() {
  const [ticketId, setTicketId] = useState('');
  const [eventId, setEventId] = useState('');
  const { result, validating, validate, reset } = useTicketValidation();

  const trimmedTicketId = ticketId.trim();
  const trimmedEventId = eventId.trim();
  const canSubmit = trimmedTicketId.length > 0 && trimmedEventId.length > 0 && !validating;

  const handleSubmit = () => {
    if (!canSubmit) return;
    void validate(trimmedTicketId, trimmedEventId);
  };

  const handleScanNext = () => {
    setTicketId('');
    setEventId('');
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
        <FormInput label="Ticket ID" value={ticketId} onChangeText={setTicketId} placeholder="ticket-..." />
        <FormInput label="Event ID" value={eventId} onChangeText={setEventId} placeholder="evento-..." />

        <Text style={styles.hint}>
          Podés pegar ambos ids desde la pantalla de detalle de la entrada en &quot;Mis entradas&quot; del cliente.
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
