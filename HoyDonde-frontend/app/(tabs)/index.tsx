import React, { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { TicketCard } from '@/components/ui/TicketCard';
import { colors, fonts, spacing } from '@/constants/theme';
import { EventResponse, eventService } from '@/services/APIService';

const PAGE_SIZE = 10;
const MESES = ['ENE', 'FEB', 'MAR', 'ABR', 'MAY', 'JUN', 'JUL', 'AGO', 'SEP', 'OCT', 'NOV', 'DIC'];

function splitFecha(iso: string): { day: string; month: string; time: string } {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return { day: '--', month: '', time: iso };
  }
  return {
    day: String(date.getDate()).padStart(2, '0'),
    month: MESES[date.getMonth()],
    time: date.toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' }),
  };
}

function precioDesde(item: EventResponse): number | null {
  if (!item.ticketGroups || item.ticketGroups.length === 0) return null;
  return Math.min(...item.ticketGroups.map((tg) => tg.precio));
}

function ediciónDeHoy(): string {
  const now = new Date();
  return `${String(now.getDate()).padStart(2, '0')}.${String(now.getMonth() + 1).padStart(2, '0')}`;
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

  const renderItem = ({ item }: { item: EventResponse }) => {
    const { day, month, time } = splitFecha(item.fechaInicio);
    const precio = precioDesde(item);

    return (
      <TicketCard
        style={styles.card}
        stub={
          <>
            <Text style={styles.stubDay}>{day}</Text>
            <Text style={styles.stubMonth}>{month}</Text>
            <Text style={styles.stubTime}>{time}</Text>
          </>
        }
      >
        <View style={styles.cardHeader}>
          <Text style={styles.eventName}>{item.nombre}</Text>
          <StatusStamp label={item.estado} tone={eventEstadoTone(item.estado)} />
        </View>

        <View style={styles.metaRow}>
          <Text style={styles.categoriaTag}>{item.categoria}</Text>
          <View style={styles.locationRow}>
            <MaterialIcons name="place" size={14} color={colors.inkSoft} />
            <Text style={styles.eventLocation}>{item.ubicacion}</Text>
          </View>
        </View>

        {item.descripcion ? (
          <Text numberOfLines={2} style={styles.eventDescription}>
            {item.descripcion}
          </Text>
        ) : null}

        {precio !== null ? (
          <View style={styles.priceTag}>
            <Text style={styles.priceLabel}>DESDE ${precio}</Text>
          </View>
        ) : null}
      </TicketCard>
    );
  };

  return (
    <View style={styles.container}>
      <View style={styles.masthead}>
        <View style={styles.eyebrowRow}>
          <Text style={styles.wordmark}>HOYDONDE?</Text>
          <View style={styles.eyebrowRight}>
            <Text style={styles.edition}>ED. {ediciónDeHoy()}</Text>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Actualizar cartelera"
              onPress={onRefresh}
              hitSlop={10}
              style={({ pressed }) => [styles.reloadButton, pressed && styles.reloadButtonPressed]}
            >
              <MaterialIcons name="refresh" size={18} color={colors.ink} />
            </Pressable>
          </View>
        </View>
        <View style={styles.hairline} />
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
          <View style={styles.retryButton}>
            <ActionButton label="Reintentar" variant="secondary" onPress={onRefresh} />
          </View>
        </View>
      ) : events.length === 0 ? (
        <View style={styles.emptyWrap}>
          <TicketCard
            orientation="column"
            style={styles.emptyCard}
            stub={
              <View style={styles.emptyStubRow}>
                <Text style={styles.emptyEyebrow}>CARTELERA VACÍA</Text>
                <MaterialIcons name="confirmation-number" size={20} color={colors.inkSoft} />
              </View>
            }
          >
            <MaterialIcons name="campaign" size={40} color={colors.ink} style={styles.emptyIcon} />
            <Text style={styles.emptyText}>No hay eventos disponibles por el momento.</Text>
            <Text style={styles.emptyHint}>
              Los organizadores todavía no publicaron nada. Volvé a mirar más tarde o actualizá la cartelera.
            </Text>
            <View style={styles.emptyButton}>
              <ActionButton label="Actualizar cartelera" onPress={onRefresh} />
            </View>
          </TicketCard>
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
  masthead: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.xxl,
    paddingBottom: spacing.md,
    borderBottomWidth: 3,
    borderBottomColor: colors.ink,
  },
  eyebrowRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  wordmark: {
    fontFamily: fonts.bold,
    fontSize: 13,
    letterSpacing: 1.5,
    color: colors.ink,
  },
  eyebrowRight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  edition: {
    fontFamily: fonts.semiBold,
    fontSize: 12,
    letterSpacing: 1,
    color: colors.inkSoft,
  },
  reloadButton: {
    borderWidth: 1,
    borderColor: colors.ink,
    borderRadius: 14,
    width: 28,
    height: 28,
    alignItems: 'center',
    justifyContent: 'center',
  },
  reloadButtonPressed: {
    backgroundColor: colors.sand,
  },
  hairline: {
    height: 1,
    backgroundColor: colors.ink,
    opacity: 0.25,
    marginTop: spacing.sm,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 34,
    color: colors.ink,
    marginTop: spacing.sm,
  },
  subtitle: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    marginTop: 2,
  },
  center: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    padding: spacing.lg,
  },
  emptyWrap: {
    flex: 1,
    padding: spacing.lg,
    justifyContent: 'center',
  },
  emptyCard: {},
  emptyStubRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  emptyEyebrow: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1.2,
    color: colors.inkSoft,
  },
  emptyIcon: {
    alignSelf: 'center',
    marginBottom: spacing.sm,
  },
  emptyText: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.ink,
    textAlign: 'center',
  },
  emptyHint: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    textAlign: 'center',
    marginTop: spacing.sm,
  },
  emptyButton: {
    marginTop: spacing.lg,
  },
  list: {
    padding: spacing.lg,
    gap: spacing.md,
  },
  card: {
    marginBottom: spacing.md,
  },
  stubDay: {
    fontFamily: fonts.black,
    fontSize: 24,
    color: colors.ink,
    lineHeight: 26,
  },
  stubMonth: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1,
    color: colors.tomato,
    marginTop: 2,
  },
  stubTime: {
    fontFamily: fonts.medium,
    fontSize: 11,
    color: colors.inkSoft,
    marginTop: 6,
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
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  categoriaTag: {
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 0.8,
    color: colors.ink,
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 2,
    paddingHorizontal: 6,
    textTransform: 'uppercase',
  },
  locationRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
  },
  eventLocation: {
    fontFamily: fonts.regular,
    color: colors.inkSoft,
    fontSize: 13,
  },
  eventDescription: {
    fontFamily: fonts.regular,
    color: colors.ink,
    marginTop: spacing.sm,
    fontSize: 14,
  },
  priceTag: {
    alignSelf: 'flex-start',
    backgroundColor: colors.lime,
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 3,
    paddingHorizontal: 8,
    marginTop: spacing.sm,
  },
  priceLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.ink,
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
  retryButton: {
    marginTop: spacing.lg,
    alignSelf: 'stretch',
  },
});
