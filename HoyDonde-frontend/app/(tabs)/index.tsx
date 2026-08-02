import React, { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, Text, View } from 'react-native';

import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import { EventResponse, eventService } from '@/services/APIService';

const PAGE_SIZE = 10;

function formatFecha(iso: string): string {
  try {
    return new Date(iso).toLocaleString('es-AR', {
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

export default function CarteleraScreen() {
  const [events, setEvents] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasNextPage, setHasNextPage] = useState(false);
  const [lastEventId, setLastEventId] = useState<string | undefined>(undefined);

  const fetchEvents = useCallback(
    async (mode: 'initial' | 'refresh' | 'more' = 'initial') => {
      if (mode === 'initial') {
        setLoading(true);
        setError(null);
      } else if (mode === 'more') {
        setLoadingMore(true);
      }

      try {
        const data = await eventService.search({
          limit: PAGE_SIZE,
          lastEventId: mode === 'more' ? lastEventId : undefined,
        });

        setEvents((prev) => (mode === 'more' ? [...prev, ...data.data] : data.data));
        setHasNextPage(data.hasNextPage);
        setLastEventId(data.lastDocumentId);
      } catch {
        if (mode !== 'more') {
          setError('No se pudieron cargar los eventos. Verificá tu conexión.');
        }
      } finally {
        setLoading(false);
        setRefreshing(false);
        setLoadingMore(false);
      }
    },
    [lastEventId]
  );

  useEffect(() => {
    fetchEvents('initial');
    // Solo en el montaje inicial: paginar/refrescar se dispara desde los handlers.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onRefresh = () => {
    setRefreshing(true);
    fetchEvents('refresh');
  };

  const loadMore = () => {
    if (hasNextPage && !loadingMore && !loading && !refreshing) {
      fetchEvents('more');
    }
  };

  const renderItem = ({ item }: { item: EventResponse }) => (
    <Surface style={styles.card}>
      <View style={styles.cardHeader}>
        <Text style={styles.eventName}>{item.nombre}</Text>
        <StatusStamp label={item.estado} tone={eventEstadoTone(item.estado)} />
      </View>
      <Text style={styles.eventDate}>{formatFecha(item.fechaInicio)}</Text>
      <Text style={styles.eventLocation}>{item.ubicacion}</Text>
      {item.descripcion ? (
        <Text numberOfLines={2} style={styles.eventDescription}>
          {item.descripcion}
        </Text>
      ) : null}
    </Surface>
  );

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Cartelera</Text>
        <Text style={styles.subtitle}>Eventos publicados</Text>
      </View>

      {loading ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={colors.tomato} />
        </View>
      ) : error ? (
        <View style={styles.center}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      ) : events.length === 0 ? (
        <View style={styles.center}>
          <Text style={styles.emptyText}>No hay eventos disponibles por el momento.</Text>
        </View>
      ) : (
        <FlatList
          data={events}
          keyExtractor={(item) => item.id}
          renderItem={renderItem}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.ink} />}
          onEndReached={loadMore}
          onEndReachedThreshold={0.5}
          ListFooterComponent={
            loadingMore ? (
              <View style={styles.footer}>
                <ActivityIndicator size="small" color={colors.tomato} />
              </View>
            ) : null
          }
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
  header: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.xxl,
    paddingBottom: spacing.md,
    borderBottomWidth: 1,
    borderBottomColor: colors.ink,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 32,
    color: colors.ink,
  },
  subtitle: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.ink,
    opacity: 0.7,
    marginTop: spacing.xs,
  },
  center: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    padding: spacing.lg,
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
    fontSize: 18,
    color: colors.ink,
    flexShrink: 1,
  },
  eventDate: {
    fontFamily: fonts.semiBold,
    color: colors.tomato,
    marginTop: spacing.sm,
  },
  eventLocation: {
    fontFamily: fonts.regular,
    color: colors.ink,
    opacity: 0.75,
    marginTop: spacing.xs,
  },
  eventDescription: {
    fontFamily: fonts.regular,
    color: colors.ink,
    marginTop: spacing.sm,
  },
  footer: {
    paddingVertical: spacing.md,
    alignItems: 'center',
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    textAlign: 'center',
  },
  emptyText: {
    fontFamily: fonts.regular,
    color: colors.ink,
    textAlign: 'center',
  },
});
