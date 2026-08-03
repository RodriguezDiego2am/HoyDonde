import React, { useCallback, useEffect, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { TicketCard } from '@/components/ui/TicketCard';
import { colors, fonts, spacing } from '@/constants/theme';
import { ApiError, EventResponse, eventService } from '@/services/APIService';
import { formatFechaHora } from '@/utils/format';

function fetchErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver tus eventos.';
    return error.message || 'No se pudieron cargar tus eventos.';
  }
  return 'No se pudieron cargar tus eventos. Verificá tu conexión.';
}

/** Panel del Organizador (API-MVP 1, GET /events/organizer/me): sus eventos, cualquier estado. */
export default function OrganizerEventsListScreen() {
  const [events, setEvents] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (mode: 'initial' | 'refresh') => {
    if (mode === 'initial') setLoading(true);
    try {
      const data = await eventService.getMyEvents();
      setEvents(data);
      setError(null);
    } catch (err) {
      setError(fetchErrorMessage(err));
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    load('initial');
  }, [load]);

  const onRefresh = () => {
    setRefreshing(true);
    load('refresh');
  };

  const openEvent = (id: string) => {
    router.push({ pathname: '/organizer/[id]', params: { id } } as unknown as Href);
  };

  const renderItem = ({ item }: { item: EventResponse }) => (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Ver detalle de ${item.nombre}`}
      onPress={() => openEvent(item.id)}
      style={({ pressed }) => pressed && styles.cardPressed}
    >
      <TicketCard
        style={styles.card}
        stub={
          <>
            <MaterialIcons name="event" size={20} color={colors.ink} />
            <Text style={styles.stubCategoria}>{item.categoria}</Text>
          </>
        }
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
          <Text style={styles.metaText}>{formatFechaHora(item.fechaInicio)}</Text>
        </View>
      </TicketCard>
    </Pressable>
  );

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ORGANIZACIÓN" title="Mis eventos" onRefresh={onRefresh} showBack />

      <View style={styles.newButtonRow}>
        <ActionButton label="+ Crear evento" onPress={() => router.push('/organizer/new')} />
      </View>

      {loading ? (
        <AsyncStateView variant="loading" message="Cargando" />
      ) : error ? (
        <AsyncStateView variant="error" message={error} onRetry={onRefresh} />
      ) : events.length === 0 ? (
        <AsyncStateView
          variant="empty"
          icon="event-note"
          message="Todavía no creaste ningún evento."
          hint='Usá "Crear evento" para empezar.'
        />
      ) : (
        <FlatList
          testID="organizer-events-list"
          data={events}
          keyExtractor={(item) => item.id}
          renderItem={renderItem}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.ink} />}
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
  newButtonRow: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.md,
  },
  list: {
    padding: spacing.lg,
    gap: spacing.md,
  },
  card: {
    marginBottom: spacing.md,
  },
  cardPressed: {
    opacity: 0.85,
  },
  stubCategoria: {
    fontFamily: fonts.bold,
    fontSize: 10,
    letterSpacing: 0.5,
    color: colors.inkSoft,
    marginTop: spacing.xs,
    textAlign: 'center',
    textTransform: 'uppercase',
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
