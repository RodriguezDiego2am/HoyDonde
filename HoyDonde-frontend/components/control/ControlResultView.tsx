import React from 'react';
import { StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { StatusStamp, StampTone, TONE_COLOR } from '@/components/ui/StatusStamp';
import { colors, fonts, spacing } from '@/constants/theme';
import { TicketValidationOutcomeKind, TicketValidationResult } from '@/services/ticketValidationService';

type IconName = React.ComponentProps<typeof MaterialIcons>['name'];

interface ResultMeta {
  headline: string;
  tone: StampTone;
  icon: IconName;
}

/**
 * Un resultado por kind (nunca solo color): headline fijo en español + ícono + tono. El texto del
 * cuerpo (`result.message`) es siempre el mensaje real devuelto por POST /api/tickets/validate
 * (API_Documentation.md §9) o, para network/unexpected, el texto armado en ticketValidationService.
 */
const RESULT_META: Record<TicketValidationOutcomeKind, ResultMeta> = {
  valid: { headline: 'Entrada válida', tone: 'success', icon: 'check-circle' },
  alreadyUsed: { headline: 'Entrada ya utilizada', tone: 'error', icon: 'history' },
  anulado: { headline: 'Entrada anulada', tone: 'error', icon: 'block' },
  eventoCancelado: { headline: 'Evento cancelado', tone: 'error', icon: 'event-busy' },
  eventoFinalizado: { headline: 'Evento finalizado', tone: 'error', icon: 'event-available' },
  notAuthorized: { headline: 'Control no autorizado', tone: 'cobalt', icon: 'gpp-bad' },
  notFound: { headline: 'Entrada no encontrada', tone: 'error', icon: 'search-off' },
  network: { headline: 'Error de conexión', tone: 'ink', icon: 'wifi-off' },
  unexpected: { headline: 'Error inesperado', tone: 'ink', icon: 'error-outline' },
};

interface ControlResultViewProps {
  result: TicketValidationResult;
  onScanNext: () => void;
  nextActionLabel?: string;
}

export function ControlResultView({ result, onScanNext, nextActionLabel = 'Escanear siguiente' }: ControlResultViewProps) {
  const meta = RESULT_META[result.kind];

  return (
    <View style={styles.container} testID="control-result">
      <View style={[styles.iconRing, { borderColor: TONE_COLOR[meta.tone] }]}>
        <MaterialIcons name={meta.icon} size={48} color={TONE_COLOR[meta.tone]} />
      </View>

      <View style={styles.stampRow}>
        <StatusStamp label={meta.headline} tone={meta.tone} />
      </View>

      <Text style={styles.message}>{result.message}</Text>

      {result.traceId ? <Text style={styles.traceId}>Código de referencia: {result.traceId}</Text> : null}

      <View style={styles.actionRow}>
        <ActionButton label={nextActionLabel} onPress={onScanNext} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: spacing.xl,
    backgroundColor: colors.paper,
  },
  iconRing: {
    width: 96,
    height: 96,
    borderRadius: 48,
    borderWidth: 3,
    alignItems: 'center',
    justifyContent: 'center',
  },
  stampRow: {
    marginTop: spacing.lg,
  },
  message: {
    fontFamily: fonts.medium,
    fontSize: 16,
    color: colors.ink,
    textAlign: 'center',
    marginTop: spacing.md,
  },
  traceId: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    textAlign: 'center',
    marginTop: spacing.sm,
  },
  actionRow: {
    alignSelf: 'stretch',
    marginTop: spacing.xl,
  },
});
