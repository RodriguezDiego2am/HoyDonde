import React, { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { StampTone, StatusStamp } from '@/components/ui/StatusStamp';
import { TicketCard } from '@/components/ui/TicketCard';
import { TicketQRModal } from '@/components/ui/TicketQRModal';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError, TicketResponse, ticketService } from '@/services/APIService';
import { formatFechaHora, formatPrecio } from '@/utils/format';

/** Estado visual del ticket: siempre con texto explícito además del color, nunca solo tono. */
function ticketDisplay(ticket: TicketResponse): { label: string; tone: StampTone } {
  if (ticket.utilizable) return { label: 'Utilizable', tone: 'success' };

  switch (ticket.motivoNoUtilizable) {
    case 'Usado':
      return { label: 'Usado', tone: 'ink' };
    case 'Anulado':
      return { label: 'Anulado', tone: 'error' };
    case 'EventoCancelado':
      return { label: 'Evento cancelado', tone: 'error' };
    case 'EventoFinalizado':
      return { label: 'Evento finalizado', tone: 'cobalt' };
    default:
      return { label: ticket.estado, tone: 'ink' };
  }
}

function fetchErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo para ver tus entradas.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver entradas.';
    return error.message || 'No se pudieron cargar tus entradas.';
  }
  return 'No se pudieron cargar tus entradas. Verificá tu conexión.';
}

export default function MisEntradasScreen() {
  const { user, initializing, hasAccion } = useAuth();

  const [tickets, setTickets] = useState<TicketResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [qrTicket, setQrTicket] = useState<TicketResponse | null>(null);

  const puedeVerEntradas = hasAccion('TICKET_VER_PROPIO');

  const load = useCallback(
    async (mode: 'initial' | 'refresh') => {
      if (mode === 'initial') setLoading(true);
      try {
        const data = await ticketService.getMine();
        setTickets(data);
        setError(null);
      } catch (err) {
        setError(fetchErrorMessage(err));
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    []
  );

  useEffect(() => {
    if (user && puedeVerEntradas) {
      load('initial');
    }
    // Solo se dispara cuando cambia la identidad/permiso efectivo del usuario.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user?.uid, puedeVerEntradas]);

  const onRefresh = () => {
    setRefreshing(true);
    load('refresh');
  };

  if (initializing) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.tomato} />
      </View>
    );
  }

  if (!user) {
    return (
      <View style={styles.safe}>
        <View style={styles.header}>
          <Text style={styles.eyebrow}>TU ARCHIVO</Text>
          <Text style={styles.title}>Mis entradas</Text>
        </View>
        <View style={styles.gateWrap}>
          <Text style={styles.gateText}>Iniciá sesión para ver las entradas que compraste.</Text>
          <View style={styles.gateButton}>
            <ActionButton
              label="Iniciar sesión"
              onPress={() => router.push({ pathname: '/login', params: { returnTo: '/(tabs)/tickets' } })}
            />
          </View>
        </View>
      </View>
    );
  }

  if (!puedeVerEntradas) {
    return (
      <View style={styles.safe}>
        <View style={styles.header}>
          <Text style={styles.eyebrow}>TU ARCHIVO</Text>
          <Text style={styles.title}>Mis entradas</Text>
        </View>
        <View style={styles.gateWrap}>
          <Text style={styles.gateText}>Tu cuenta no tiene permiso para ver entradas.</Text>
        </View>
      </View>
    );
  }

  const renderItem = ({ item }: { item: TicketResponse }) => {
    const display = ticketDisplay(item);

    return (
      <TicketCard
        style={styles.card}
        stub={
          <>
            <MaterialIcons name="confirmation-number" size={20} color={colors.ink} />
            <Text style={styles.stubPrecio}>{formatPrecio(item.precioPagado)}</Text>
          </>
        }
      >
        <View style={styles.cardHeader}>
          <Text style={styles.eventName} numberOfLines={2}>
            {item.eventoNombre}
          </Text>
          <StatusStamp label={display.label} tone={display.tone} />
        </View>

        <Text style={styles.ticketType}>{item.ticketTypeNombre}</Text>

        <View style={styles.metaRow}>
          <MaterialIcons name="event" size={14} color={colors.inkSoft} />
          <Text style={styles.metaText}>{formatFechaHora(item.fechaInicio)}</Text>
        </View>
        <View style={styles.metaRow}>
          <MaterialIcons name="receipt-long" size={14} color={colors.inkSoft} />
          <Text style={styles.metaText}>Comprada el {formatFechaHora(item.fechaCompra)}</Text>
        </View>

        <View style={styles.qrButtonRow}>
          <ActionButton label="Ver QR" variant="secondary" onPress={() => setQrTicket(item)} />
        </View>
      </TicketCard>
    );
  };

  return (
    <View style={styles.safe}>
      <View style={styles.header}>
        <View style={styles.headerTopRow}>
          <Text style={styles.eyebrow}>TU ARCHIVO</Text>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Actualizar mis entradas"
            onPress={onRefresh}
            hitSlop={10}
            style={({ pressed }) => [styles.reloadButton, pressed && styles.reloadButtonPressed]}
          >
            <MaterialIcons name="refresh" size={18} color={colors.ink} />
          </Pressable>
        </View>
        <Text style={styles.title}>Mis entradas</Text>
      </View>

      {loading ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={colors.tomato} />
        </View>
      ) : error ? (
        <View style={styles.center}>
          <Text style={styles.errorText}>{error}</Text>
          <View style={styles.gateButton}>
            <ActionButton label="Reintentar" variant="secondary" onPress={onRefresh} />
          </View>
        </View>
      ) : tickets.length === 0 ? (
        <View style={styles.gateWrap}>
          <MaterialIcons name="confirmation-number" size={36} color={colors.inkSoft} />
          <Text style={styles.gateText}>Todavía no compraste ninguna entrada.</Text>
        </View>
      ) : (
        <FlatList
          testID="tickets-list"
          data={tickets}
          keyExtractor={(item) => item.id}
          renderItem={renderItem}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.ink} />}
        />
      )}

      {qrTicket ? (
        <TicketQRModal
          visible
          ticketId={qrTicket.id}
          eventId={qrTicket.eventoId}
          eventoNombre={qrTicket.eventoNombre}
          onClose={() => setQrTicket(null)}
        />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  safe: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  center: {
    flex: 1,
    backgroundColor: colors.paper,
    justifyContent: 'center',
    alignItems: 'center',
    padding: spacing.lg,
    gap: spacing.sm,
  },
  header: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.xxl,
    paddingBottom: spacing.md,
    borderBottomWidth: 3,
    borderBottomColor: colors.ink,
  },
  headerTopRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
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
  eyebrow: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 1.2,
    color: colors.inkSoft,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 32,
    color: colors.ink,
    marginTop: spacing.sm,
  },
  gateWrap: {
    flex: 1,
    padding: spacing.lg,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.md,
  },
  gateText: {
    fontFamily: fonts.medium,
    fontSize: 15,
    color: colors.ink,
    textAlign: 'center',
  },
  gateButton: {
    marginTop: spacing.sm,
    alignSelf: 'stretch',
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    textAlign: 'center',
  },
  list: {
    padding: spacing.lg,
    gap: spacing.md,
  },
  card: {
    marginBottom: spacing.md,
  },
  stubPrecio: {
    fontFamily: fonts.bold,
    fontSize: 11,
    color: colors.ink,
    marginTop: spacing.xs,
    textAlign: 'center',
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
  ticketType: {
    fontFamily: fonts.semiBold,
    fontSize: 13,
    color: colors.tomato,
    marginTop: 2,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
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
  qrButtonRow: {
    marginTop: spacing.md,
    alignSelf: 'flex-start',
  },
});
