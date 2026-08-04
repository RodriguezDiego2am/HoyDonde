import React, { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { PurchaseReceiptPanel } from '@/components/purchase/PurchaseReceiptPanel';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import {
  ApiError,
  CompraResponse,
  EventResponse,
  TicketTypeResponse,
  eventService,
  ticketService,
} from '@/services/APIService';
import { formatFechaHora, formatPrecio } from '@/utils/format';

const MAX_POR_COMPRA = 10;

function purchaseErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'EVENT_NOT_AVAILABLE_FOR_PURCHASE':
        return 'Este evento ya no está disponible para la compra (todavía no publicado, o ya empezó).';
      case 'TICKET_STOCK_INSUFFICIENT':
        return 'No queda stock suficiente para la cantidad elegida. Probá con menos entradas.';
      case 'TICKET_TYPE_INVALID':
        return 'Este tipo de entrada ya no está disponible. Volvé a cargar el evento.';
      case 'EVENT_NOT_FOUND':
        return 'El evento ya no existe.';
      case 'VALIDATION_ERROR': {
        const first = error.errors ? Object.values(error.errors).flat()[0] : undefined;
        return first ?? error.message;
      }
      default:
        break;
    }
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo para comprar.';
    if (error.isForbidden) return 'No tenés permiso para comprar entradas con esta cuenta.';
    return error.message || 'No se pudo completar la compra.';
  }
  return 'No se pudo completar la compra. Intentá de nuevo.';
}

type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

export default function EventDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { user, initializing, hasAccion } = useAuth();

  const [event, setEvent] = useState<EventResponse | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('loading');

  const [selectedTicketTypeId, setSelectedTicketTypeId] = useState<string | null>(null);
  const [cantidad, setCantidad] = useState(1);
  const [reviewing, setReviewing] = useState(false);
  const [buying, setBuying] = useState(false);
  const [purchaseError, setPurchaseError] = useState<string | null>(null);
  const [purchasedCompra, setPurchasedCompra] = useState<CompraResponse | null>(null);

  const loadEvent = useCallback(async () => {
    if (!id) return;
    setLoadState('loading');
    try {
      const data = await eventService.getById(id);
      setEvent(data);
      setLoadState('ready');
      setSelectedTicketTypeId((prev) => prev ?? data.ticketGroups.find((tg) => tg.cantidadDisponible > 0)?.id ?? null);
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setLoadState('not-found');
      } else {
        setLoadState('error');
      }
    }
  }, [id]);

  useEffect(() => {
    loadEvent();
  }, [loadEvent]);

  const selectedTicketType: TicketTypeResponse | undefined = event?.ticketGroups.find(
    (tg) => tg.id === selectedTicketTypeId
  );

  const maxCantidad = selectedTicketType ? Math.min(MAX_POR_COMPRA, selectedTicketType.cantidadDisponible) : 1;
  const esGratis = selectedTicketType?.precio === 0;
  const obtenerLabel = esGratis ? 'Obtener entrada' : 'Comprar entradas';
  const confirmarLabel = esGratis ? 'Obtener entrada' : 'Confirmar compra';

  const selectTicketType = (ticketTypeId: string) => {
    setSelectedTicketTypeId(ticketTypeId);
    setCantidad(1);
    setReviewing(false);
    setPurchaseError(null);
  };

  const changeCantidad = (delta: number) => {
    setCantidad((prev) => Math.min(Math.max(1, prev + delta), Math.max(1, maxCantidad)));
  };

  const startReview = () => {
    if (!selectedTicketType) return;
    setPurchaseError(null);
    setReviewing(true);
  };

  const cancelReview = () => {
    setReviewing(false);
  };

  const confirmPurchase = async () => {
    if (!event || !selectedTicketType || buying) return;
    setBuying(true);
    setPurchaseError(null);
    try {
      const compra = await ticketService.buy({
        eventoId: event.id,
        ticketTypeId: selectedTicketType.id,
        cantidad,
      });
      setPurchasedCompra(compra);
      setReviewing(false);
    } catch (error) {
      setPurchaseError(purchaseErrorMessage(error));
    } finally {
      setBuying(false);
    }
  };

  const goToLogin = () => {
    router.push({ pathname: '/login', params: { returnTo: id ? `/events/${id}` : '/(tabs)' } });
  };

  const goToTickets = () => {
    router.replace('/(tabs)/tickets' as Href);
  };

  return (
    <View style={styles.container}>
      <View style={styles.topBar}>
        <ActionButton label="← Volver" variant="ghost" onPress={() => router.back()} />
      </View>

      {loadState === 'loading' ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={colors.tomato} />
        </View>
      ) : loadState === 'not-found' ? (
        <View style={styles.center}>
          <MaterialIcons name="search-off" size={40} color={colors.inkSoft} />
          <Text style={styles.stateTitle}>Evento no encontrado</Text>
          <Text style={styles.stateHint}>Puede que ya no esté publicado o el enlace sea incorrecto.</Text>
        </View>
      ) : loadState === 'error' ? (
        <View style={styles.center}>
          <Text style={styles.stateTitle}>No pudimos cargar este evento</Text>
          <Text style={styles.stateHint}>Verificá tu conexión e intentá de nuevo.</Text>
          <View style={styles.retryButton}>
            <ActionButton label="Reintentar" variant="secondary" onPress={loadEvent} />
          </View>
        </View>
      ) : event ? (
        <ScrollView contentContainerStyle={styles.scroll}>
          <View style={styles.masthead}>
            <View style={styles.mastheadTopRow}>
              <Text style={styles.categoriaEyebrow}>{event.categoria}</Text>
              <StatusStamp label={event.estado} tone={eventEstadoTone(event.estado)} />
            </View>
            <Text style={styles.title}>{event.nombre}</Text>

            <View style={styles.metaRow}>
              <MaterialIcons name="place" size={16} color={colors.inkSoft} />
              <Text style={styles.metaText}>{event.ubicacion}</Text>
            </View>
            <View style={styles.metaRow}>
              <MaterialIcons name="event" size={16} color={colors.inkSoft} />
              <Text style={styles.metaText}>Desde {formatFechaHora(event.fechaInicio)}</Text>
            </View>
            <View style={styles.metaRow}>
              <MaterialIcons name="event-available" size={16} color={colors.inkSoft} />
              <Text style={styles.metaText}>Hasta {formatFechaHora(event.fechaFin)}</Text>
            </View>

            {event.descripcion ? <Text style={styles.description}>{event.descripcion}</Text> : null}
          </View>

          <SectionDivider index="01" label="Entradas" style={styles.sectionMargin} />
          <View style={styles.ticketList}>
            {event.ticketGroups.length === 0 ? (
              <Text style={styles.stateHint}>Este evento todavía no tiene tipos de entrada cargados.</Text>
            ) : (
              event.ticketGroups.map((tg) => {
                const agotado = tg.cantidadDisponible <= 0;
                const selected = selectedTicketTypeId === tg.id;
                return (
                  <View
                    key={tg.id}
                    style={[styles.ticketRow, selected && styles.ticketRowSelected, agotado && styles.ticketRowDisabled]}
                  >
                    <View style={styles.ticketRowInfo}>
                      <Text style={styles.ticketRowName}>{tg.nombre}</Text>
                      <Text style={styles.ticketRowStock}>
                        {agotado ? 'Agotado' : `${tg.cantidadDisponible} disponibles`}
                      </Text>
                    </View>
                    <Text style={styles.ticketRowPrice}>{formatPrecio(tg.precio)}</Text>
                  </View>
                );
              })
            )}
          </View>

          <SectionDivider index="02" label="Comprar" style={styles.sectionMargin} />
          {purchasedCompra !== null ? (
            <View style={styles.purchaseSuccessWrap}>
              <View style={styles.purchaseSuccessBanner}>
                <MaterialIcons name="check-circle" size={28} color={colors.success} />
                <Text style={styles.purchaseSuccessTitle}>
                  {purchasedCompra.cantidadEntradas === 1
                    ? 'Entrada comprada'
                    : `${purchasedCompra.cantidadEntradas} entradas compradas`}
                </Text>
              </View>
              <PurchaseReceiptPanel compra={purchasedCompra} onGoToTickets={goToTickets} />
            </View>
          ) : initializing ? (
            <View style={styles.center}>
              <ActivityIndicator color={colors.tomato} />
            </View>
          ) : !user ? (
            <Surface style={styles.purchaseGate}>
              <Text style={styles.stateHint}>Iniciá sesión para comprar entradas de este evento.</Text>
              <View style={styles.retryButton}>
                <ActionButton label="Iniciar sesión" onPress={goToLogin} />
              </View>
            </Surface>
          ) : !hasAccion('TICKET_COMPRAR') ? (
            <Surface style={styles.purchaseGate}>
              <Text style={styles.stateHint}>Tu cuenta no tiene habilitada la compra de entradas.</Text>
            </Surface>
          ) : !selectedTicketType ? (
            <Surface style={styles.purchaseGate}>
              <Text style={styles.stateHint}>No hay entradas disponibles para comprar en este momento.</Text>
            </Surface>
          ) : (
            <Surface style={styles.purchasePanel}>
              <Text style={styles.purchaseLabel}>Tipo de entrada</Text>
              <View style={styles.typeChipRow}>
                {event.ticketGroups.map((tg) => {
                  const agotado = tg.cantidadDisponible <= 0;
                  const selected = selectedTicketTypeId === tg.id;
                  return (
                    <ActionButton
                      key={tg.id}
                      label={agotado ? `${tg.nombre} (agotado)` : tg.nombre}
                      variant={selected ? 'primary' : 'secondary'}
                      disabled={agotado}
                      onPress={() => selectTicketType(tg.id)}
                    />
                  );
                })}
              </View>

              <Text style={[styles.purchaseLabel, styles.purchaseLabelSpacing]}>Cantidad</Text>
              <View style={styles.stepperRow}>
                <ActionButton label="−" variant="secondary" onPress={() => changeCantidad(-1)} disabled={cantidad <= 1} />
                <Text testID="stepper-value" style={styles.stepperValue}>
                  {cantidad}
                </Text>
                <ActionButton
                  label="+"
                  variant="secondary"
                  onPress={() => changeCantidad(1)}
                  disabled={cantidad >= maxCantidad}
                />
              </View>
              <Text style={styles.stockHint}>Máximo {maxCantidad} por esta operación.</Text>

              <View style={styles.summary}>
                <View style={styles.summaryRow}>
                  <Text style={styles.summaryLabel}>Precio unitario</Text>
                  <Text style={styles.summaryValue}>{formatPrecio(selectedTicketType.precio)}</Text>
                </View>
                <View style={styles.summaryRow}>
                  <Text style={styles.summaryLabel}>Cantidad</Text>
                  <Text style={styles.summaryValue}>{cantidad}</Text>
                </View>
                <View style={styles.summaryHairline} />
                <View style={styles.summaryRow}>
                  <Text style={styles.summaryTotalLabel}>Total</Text>
                  <Text style={styles.summaryTotalValue}>{formatPrecio(selectedTicketType.precio * cantidad)}</Text>
                </View>
              </View>

              <Text style={styles.demoNotice}>
                {esGratis
                  ? 'Entrada gratuita: no requiere pago.'
                  : 'Compra de demostración: no se realizará ningún cobro.'}
              </Text>

              {purchaseError ? <Text style={styles.errorText}>{purchaseError}</Text> : null}

              {!reviewing ? (
                <View style={styles.retryButton}>
                  <ActionButton label={obtenerLabel} onPress={startReview} />
                </View>
              ) : (
                <Surface variant="sand" style={styles.confirmBox}>
                  <Text style={styles.confirmTitle}>Confirmá tu compra</Text>
                  <Text style={styles.stateHint}>
                    {cantidad} × {selectedTicketType.nombre} — {formatPrecio(selectedTicketType.precio * cantidad)}
                  </Text>
                  <Text style={styles.demoNotice}>
                    {esGratis
                      ? 'Entrada gratuita: no requiere pago.'
                      : 'Compra de demostración: no se realizará ningún cobro.'}
                  </Text>
                  <View style={styles.confirmActions}>
                    <View style={styles.confirmActionButton}>
                      <ActionButton label="Cancelar" variant="ghost" onPress={cancelReview} disabled={buying} />
                    </View>
                    <View style={styles.confirmActionButton}>
                      <ActionButton label={confirmarLabel} onPress={confirmPurchase} loading={buying} />
                    </View>
                  </View>
                </Surface>
              )}
            </Surface>
          )}
        </ScrollView>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  topBar: {
    paddingHorizontal: spacing.md,
    paddingTop: spacing.xxl,
    alignItems: 'flex-start',
  },
  center: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    padding: spacing.lg,
    gap: spacing.sm,
  },
  stateTitle: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.ink,
    textAlign: 'center',
  },
  stateHint: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
    textAlign: 'center',
  },
  retryButton: {
    marginTop: spacing.md,
    alignSelf: 'stretch',
  },
  scroll: {
    padding: spacing.lg,
    paddingTop: spacing.sm,
  },
  masthead: {
    borderBottomWidth: borderWidth.thick,
    borderBottomColor: colors.ink,
    paddingBottom: spacing.lg,
  },
  mastheadTopRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  categoriaEyebrow: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1.2,
    color: colors.tomato,
    textTransform: 'uppercase',
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 30,
    color: colors.ink,
    marginTop: spacing.sm,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginTop: spacing.sm,
  },
  metaText: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.inkSoft,
  },
  description: {
    fontFamily: fonts.regular,
    fontSize: 15,
    color: colors.ink,
    marginTop: spacing.md,
    lineHeight: 21,
  },
  sectionMargin: {
    marginTop: spacing.lg,
  },
  ticketList: {
    gap: spacing.sm,
  },
  ticketRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.sand,
  },
  ticketRowSelected: {
    borderWidth: borderWidth.thick,
    backgroundColor: colors.lime,
  },
  ticketRowDisabled: {
    opacity: 0.5,
  },
  ticketRowInfo: {
    flexShrink: 1,
  },
  ticketRowName: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
  },
  ticketRowStock: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    marginTop: 2,
  },
  ticketRowPrice: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
  },
  purchaseGate: {
    alignItems: 'center',
    gap: spacing.sm,
  },
  purchaseSuccessWrap: {
    gap: spacing.md,
  },
  purchaseSuccessBanner: {
    alignItems: 'center',
    gap: spacing.xs,
  },
  purchaseSuccessTitle: {
    fontFamily: fonts.bold,
    fontSize: 18,
    color: colors.ink,
  },
  purchasePanel: {},
  purchaseLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    color: colors.inkSoft,
  },
  purchaseLabelSpacing: {
    marginTop: spacing.lg,
  },
  typeChipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  stepperRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    marginTop: spacing.sm,
  },
  stepperValue: {
    fontFamily: fonts.black,
    fontSize: 22,
    color: colors.ink,
    minWidth: 32,
    textAlign: 'center',
  },
  stockHint: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    marginTop: spacing.xs,
  },
  summary: {
    marginTop: spacing.lg,
  },
  summaryRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginTop: spacing.xs,
  },
  summaryLabel: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
  },
  summaryValue: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.ink,
  },
  summaryHairline: {
    height: 1,
    backgroundColor: colors.ink,
    opacity: 0.25,
    marginVertical: spacing.sm,
  },
  summaryTotalLabel: {
    fontFamily: fonts.bold,
    fontSize: 16,
    color: colors.ink,
  },
  summaryTotalValue: {
    fontFamily: fonts.black,
    fontSize: 18,
    color: colors.tomato,
  },
  demoNotice: {
    fontFamily: fonts.semiBold,
    fontSize: 13,
    color: colors.cobalt,
    marginTop: spacing.md,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.error,
    marginTop: spacing.md,
  },
  confirmBox: {
    marginTop: spacing.md,
    gap: spacing.xs,
  },
  confirmTitle: {
    fontFamily: fonts.bold,
    fontSize: 16,
    color: colors.ink,
  },
  confirmActions: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  confirmActionButton: {
    flex: 1,
  },
});
