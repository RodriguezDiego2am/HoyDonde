import React, { useCallback, useEffect, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { TicketCard } from '@/components/ui/TicketCard';
import { CategoriaOption, EventFilterPanel } from '@/components/cartelera/EventFilterPanel';
import { colors, fonts, spacing } from '@/constants/theme';
import { EventResponse, eventService } from '@/services/APIService';
import { formatPrecio } from '@/utils/format';
import { isValidLocalDateRange, nextLocalDayExclusive, parseLocalDate, startOfLocalDay, toUtcIso } from '@/utils/datetime';

const PAGE_SIZE = 10;
const MESES = ['ENE', 'FEB', 'MAR', 'ABR', 'MAY', 'JUN', 'JUL', 'AGO', 'SEP', 'OCT', 'NOV', 'DIC'];

/** Categorías reales del enum Event.EventCategory (HoyDonde.API/Models/Event.cs) — el filtro
 * nunca inventa valores propios, solo los que la API acepta en `categoria`. */
const CATEGORIAS: CategoriaOption[] = [
  { value: 'Musica', label: 'Música' },
  { value: 'Deportes', label: 'Deportes' },
  { value: 'Tecnologia', label: 'Tecnología' },
  { value: 'Arte', label: 'Arte' },
  { value: 'Otros', label: 'Otros' },
];

/** Filtros ya aplicados (tras "Aplicar filtros"): fechaDesde/fechaHasta van en ISO UTC, listos
 * para el contrato real de GET /api/events; los campos *Display conservan "DD/MM/AAAA" solo para
 * mostrar el resumen y precargar el panel si se reabre. */
interface AppliedFilters {
  categoria?: string;
  ubicacion?: string;
  fechaDesde?: string;
  fechaHasta?: string;
  fechaDesdeDisplay?: string;
  fechaHastaDisplay?: string;
}

const SIN_FILTROS: AppliedFilters = {};

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

/** Combina una página nueva con la lista actual sin duplicar ids (reintentos/reconexión). */
function mergeSinDuplicados(prev: EventResponse[], next: EventResponse[]): EventResponse[] {
  const seen = new Set(prev.map((e) => e.id));
  return [...prev, ...next.filter((e) => !seen.has(e.id))];
}

function contarFiltrosActivos(filters: AppliedFilters): number {
  let count = 0;
  if (filters.categoria) count += 1;
  if (filters.ubicacion) count += 1;
  if (filters.fechaDesde || filters.fechaHasta) count += 1;
  return count;
}

function resumenFiltros(filters: AppliedFilters, categorias: CategoriaOption[]): string {
  const partes: string[] = [];
  if (filters.categoria) {
    partes.push(categorias.find((c) => c.value === filters.categoria)?.label ?? filters.categoria);
  }
  if (filters.ubicacion) {
    partes.push(filters.ubicacion);
  }
  if (filters.fechaDesdeDisplay && filters.fechaHastaDisplay) {
    partes.push(`${filters.fechaDesdeDisplay} – ${filters.fechaHastaDisplay}`);
  } else if (filters.fechaDesdeDisplay) {
    partes.push(`Desde ${filters.fechaDesdeDisplay}`);
  } else if (filters.fechaHastaDisplay) {
    partes.push(`Hasta ${filters.fechaHastaDisplay}`);
  }
  return partes.join(' · ');
}

export default function CarteleraScreen() {
  const [events, setEvents] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasNextPage, setHasNextPage] = useState(false);
  const [lastEventId, setLastEventId] = useState<string | undefined>(undefined);

  const [appliedFilters, setAppliedFilters] = useState<AppliedFilters>(SIN_FILTROS);

  const [filtersOpen, setFiltersOpen] = useState(false);
  const [categoriaDraft, setCategoriaDraft] = useState<string | undefined>(undefined);
  const [ubicacionDraft, setUbicacionDraft] = useState('');
  const [fechaDesdeDraft, setFechaDesdeDraft] = useState('');
  const [fechaHastaDraft, setFechaHastaDraft] = useState('');
  const [filterError, setFilterError] = useState<string | null>(null);

  // Cerrojo síncrono adicional a los estados de loading: onEndReached puede dispararse más de
  // una vez antes de que el re-render con loadingMore=true se aplique, y esto evita una segunda
  // request concurrente en esa ventana.
  const fetchInFlight = useRef(false);

  const fetchEvents = useCallback(
    async (mode: 'initial' | 'refresh' | 'more', filters: AppliedFilters, cursor: string | undefined) => {
      if (fetchInFlight.current) return;
      fetchInFlight.current = true;

      if (mode === 'initial') {
        setLoading(true);
      } else if (mode === 'more') {
        setLoadingMore(true);
      }

      try {
        const data = await eventService.search({
          limit: PAGE_SIZE,
          lastEventId: mode === 'more' ? cursor : undefined,
          categoria: filters.categoria,
          ubicacion: filters.ubicacion,
          fechaDesde: filters.fechaDesde,
          fechaHasta: filters.fechaHasta,
        });

        setEvents((prev) => (mode === 'more' ? mergeSinDuplicados(prev, data.data) : data.data));
        setHasNextPage(data.hasNextPage);
        setLastEventId(data.lastDocumentId);
        if (mode !== 'more') {
          setError(null);
        }
      } catch {
        if (mode !== 'more') {
          setError('No se pudieron cargar los eventos. Verificá tu conexión.');
        }
      } finally {
        setLoading(false);
        setRefreshing(false);
        setLoadingMore(false);
        fetchInFlight.current = false;
      }
    },
    []
  );

  useEffect(() => {
    fetchEvents('initial', SIN_FILTROS, undefined);
    // Se dispara solo al montar: cambiar/aplicar/limpiar filtros dispara su propio fetch abajo.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onRefresh = () => {
    if (fetchInFlight.current) return;
    setRefreshing(true);
    fetchEvents('refresh', appliedFilters, undefined);
  };

  const loadMore = () => {
    if (hasNextPage && !fetchInFlight.current) {
      fetchEvents('more', appliedFilters, lastEventId);
    }
  };

  const openFiltersPanel = () => {
    setCategoriaDraft(appliedFilters.categoria);
    setUbicacionDraft(appliedFilters.ubicacion ?? '');
    setFechaDesdeDraft(appliedFilters.fechaDesdeDisplay ?? '');
    setFechaHastaDraft(appliedFilters.fechaHastaDisplay ?? '');
    setFilterError(null);
    setFiltersOpen(true);
  };

  const handleApplyFilters = () => {
    if (fetchInFlight.current) return;

    // SegmentedDateField siempre emite "DD/MM/AAAA" con los segmentos vacíos rellenados por sus
    // propios separadores (p. ej. "//" cuando no se tipeó nada): sin dígitos, nunca es un string
    // realmente vacío. Sin este chequeo, un campo intacto se interpretaría como "fecha inválida"
    // en vez de "no se pidió filtro de fecha".
    const desdeTexto = fechaDesdeDraft.trim();
    const hastaTexto = fechaHastaDraft.trim();
    const desdeProvista = /\d/.test(desdeTexto);
    const hastaProvista = /\d/.test(hastaTexto);
    const desdeDate = desdeProvista ? parseLocalDate(desdeTexto) : null;
    const hastaDate = hastaProvista ? parseLocalDate(hastaTexto) : null;

    if (desdeProvista && !desdeDate) {
      setFilterError('Ingresá una fecha "Desde" válida (DD/MM/AAAA).');
      return;
    }
    if (hastaProvista && !hastaDate) {
      setFilterError('Ingresá una fecha "Hasta" válida (DD/MM/AAAA).');
      return;
    }
    if (desdeDate && hastaDate && !isValidLocalDateRange(desdeDate, hastaDate)) {
      setFilterError('"Desde" no puede ser posterior a "Hasta".');
      return;
    }

    setFilterError(null);

    const nextFilters: AppliedFilters = {
      categoria: categoriaDraft,
      ubicacion: ubicacionDraft.trim() || undefined,
      fechaDesde: desdeDate ? toUtcIso(startOfLocalDay(desdeDate)) : undefined,
      fechaHasta: hastaDate ? toUtcIso(nextLocalDayExclusive(hastaDate)) : undefined,
      fechaDesdeDisplay: desdeDate ? desdeTexto : undefined,
      fechaHastaDisplay: hastaDate ? hastaTexto : undefined,
    };

    setAppliedFilters(nextFilters);
    setFiltersOpen(false);
    fetchEvents('initial', nextFilters, undefined);
  };

  const handleClearFilters = () => {
    if (fetchInFlight.current) return;
    setCategoriaDraft(undefined);
    setUbicacionDraft('');
    setFechaDesdeDraft('');
    setFechaHastaDraft('');
    setFilterError(null);
    setAppliedFilters(SIN_FILTROS);
    setFiltersOpen(false);
    fetchEvents('initial', SIN_FILTROS, undefined);
  };

  const openEvent = (id: string) => {
    router.push({ pathname: '/events/[id]', params: { id } });
  };

  const filtrosActivos = contarFiltrosActivos(appliedFilters);
  const tieneFiltrosActivos = filtrosActivos > 0;

  const renderItem = ({ item }: { item: EventResponse }) => {
    const { day, month, time } = splitFecha(item.fechaInicio);
    const precio = precioDesde(item);

    return (
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
              <Text style={styles.priceLabel}>DESDE {formatPrecio(precio)}</Text>
            </View>
          ) : null}
        </TicketCard>
      </Pressable>
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

        <View style={styles.filtersBar}>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Abrir filtros"
            onPress={openFiltersPanel}
            style={({ pressed }) => [styles.filtersButton, pressed && styles.filtersButtonPressed]}
          >
            <MaterialIcons name="tune" size={16} color={colors.ink} />
            <Text style={styles.filtersButtonText}>Filtros</Text>
            {filtrosActivos > 0 ? (
              <View style={styles.filtersBadge}>
                <Text style={styles.filtersBadgeText}>{filtrosActivos}</Text>
              </View>
            ) : null}
          </Pressable>

          {tieneFiltrosActivos ? (
            <View style={styles.filtersSummaryWrap}>
              <Text numberOfLines={1} style={styles.filtersSummaryText}>
                {resumenFiltros(appliedFilters, CATEGORIAS)}
              </Text>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Limpiar filtros"
                onPress={handleClearFilters}
                hitSlop={8}
              >
                <MaterialIcons name="close" size={16} color={colors.inkSoft} />
              </Pressable>
            </View>
          ) : null}
        </View>
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
            <Text style={styles.emptyText}>
              {tieneFiltrosActivos
                ? 'No encontramos eventos con estos filtros.'
                : 'No hay eventos publicados por el momento.'}
            </Text>
            <Text style={styles.emptyHint}>
              {tieneFiltrosActivos
                ? 'Probá ajustar o limpiar los filtros para ver más resultados.'
                : 'Los organizadores todavía no publicaron nada acá. Volvé a mirar más tarde o actualizá la cartelera.'}
            </Text>
            <View style={styles.emptyButton}>
              {tieneFiltrosActivos ? (
                <ActionButton label="Limpiar filtros" onPress={handleClearFilters} />
              ) : (
                <ActionButton label="Actualizar cartelera" onPress={onRefresh} />
              )}
            </View>
          </TicketCard>
        </View>
      ) : (
        <FlatList
          testID="cartelera-list"
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

      <EventFilterPanel
        visible={filtersOpen}
        onClose={() => setFiltersOpen(false)}
        categorias={CATEGORIAS}
        categoria={categoriaDraft}
        onChangeCategoria={setCategoriaDraft}
        ubicacion={ubicacionDraft}
        onChangeUbicacion={setUbicacionDraft}
        fechaDesde={fechaDesdeDraft}
        onChangeFechaDesde={setFechaDesdeDraft}
        fechaHasta={fechaHastaDraft}
        onChangeFechaHasta={setFechaHastaDraft}
        error={filterError}
        onApply={handleApplyFilters}
        onClear={handleClearFilters}
      />
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
  filtersBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginTop: spacing.md,
    flexWrap: 'wrap',
  },
  filtersButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 6,
    paddingHorizontal: 12,
  },
  filtersButtonPressed: {
    backgroundColor: colors.sand,
  },
  filtersButtonText: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.ink,
    textTransform: 'uppercase',
  },
  filtersBadge: {
    backgroundColor: colors.tomato,
    borderRadius: 10,
    minWidth: 18,
    height: 18,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  filtersBadgeText: {
    fontFamily: fonts.bold,
    fontSize: 11,
    color: colors.paper,
  },
  filtersSummaryWrap: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    flexShrink: 1,
  },
  filtersSummaryText: {
    fontFamily: fonts.medium,
    fontSize: 12,
    color: colors.inkSoft,
    flexShrink: 1,
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
  cardPressed: {
    opacity: 0.85,
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
