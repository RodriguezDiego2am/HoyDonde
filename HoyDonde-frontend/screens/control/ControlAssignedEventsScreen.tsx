import React, { useCallback, useEffect, useState } from 'react';
import { FlatList, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { TicketCard } from '@/components/ui/TicketCard';
import { colors, fonts, spacing } from '@/constants/theme';
import { ApiError } from '@/services/apiError';
import { EventoAsignadoResponse, controlAsignacionService } from '@/services/controlAsignacionService';
import { formatFechaHora } from '@/utils/format';

function fetchErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver eventos de Control.';
    return error.message || 'No se pudieron cargar tus eventos asignados.';
  }
  return 'No se pudieron cargar tus eventos asignados. Verificá tu conexión.';
}

/**
 * Pantalla mínima de Control (API-MVP 5, GET /events/control/me): confirma que la cuenta ve el
 * evento correcto. El escaneo/validación de QR queda para el siguiente bloque (fuera de alcance).
 */
export default function ControlAssignedEventsScreen() {
  const [events, setEvents] = useState<EventoAsignadoResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await controlAsignacionService.getMyAssignedEvents();
      setEvents(data);
      setError(null);
    } catch (err) {
      setError(fetchErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const renderItem = ({ item }: { item: EventoAsignadoResponse }) => (
    <TicketCard
      style={styles.card}
      stub={<MaterialIcons name="verified-user" size={22} color={colors.ink} />}
    >
      <View style={styles.cardHeader}>
        <Text style={styles.eventName} numberOfLines={2}>
          {item.nombre}
        </Text>
        <StatusStamp label={item.estado} tone={eventEstadoTone(item.estado)} />
      </View>
      <View style={styles.metaRow}>
        <MaterialIcons name="place" size={14} color={colors.inkSoft} />
        <Text style={styles.metaText}>{item.ubicacion}</Text>
      </View>
      <View style={styles.metaRow}>
        <MaterialIcons name="event" size={14} color={colors.inkSoft} />
        <Text style={styles.metaText}>Desde {formatFechaHora(item.fechaInicio)}</Text>
      </View>
      <View style={styles.metaRow}>
        <MaterialIcons name="event-available" size={14} color={colors.inkSoft} />
        <Text style={styles.metaText}>Hasta {formatFechaHora(item.fechaFin)}</Text>
      </View>
    </TicketCard>
  );

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="CONTROL" title="Mis eventos" onRefresh={load} showBack />

      {loading ? (
        <AsyncStateView variant="loading" message="Cargando" />
      ) : error ? (
        <AsyncStateView variant="error" message={error} onRetry={load} />
      ) : events.length === 0 ? (
        <AsyncStateView
          variant="empty"
          icon="event-busy"
          message="Todavía no te asignaron a ningún evento."
          hint="Pedile al organizador que te asigne desde su panel."
        />
      ) : (
        <FlatList
          testID="control-assigned-events-list"
          data={events}
          keyExtractor={(item) => item.eventId}
          renderItem={renderItem}
          contentContainerStyle={styles.list}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  list: {
    padding: spacing.lg,
    gap: spacing.md,
  },
  card: {
    marginBottom: spacing.md,
  },
  cardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  eventName: {
    fontFamily: fonts.bold,
    fontSize: 17,
    color: colors.ink,
    flexShrink: 1,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    marginTop: spacing.sm,
  },
  metaText: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
  },
});
