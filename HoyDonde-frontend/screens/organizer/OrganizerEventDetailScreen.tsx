import React, { useCallback, useEffect, useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { ACCIONES } from '@/constants/acciones';
import { useAuth } from '@/context/AuthContext';
import { ApiError, EventResponse, eventService } from '@/services/APIService';
import { ControlAsignadoResponse, controlAsignacionService } from '@/services/controlAsignacionService';
import { formatFechaHora, formatPrecio } from '@/utils/format';

type LoadState = 'loading' | 'ready' | 'error';
type ConfirmActionKind = 'publish' | 'cancel' | null;

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) return 'El evento ya no existe.';
    if (error.isForbidden) return 'No tenés permiso para ver este evento.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudo cargar el evento.';
  }
  return 'No se pudo cargar el evento. Verificá tu conexión.';
}

function actionErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'EVENT_INVALID_TRANSITION':
        return 'Ese cambio de estado ya no es válido para este evento.';
      case 'EVENT_MISSING_TICKET_TYPES':
        return 'Agregá al menos un tipo de entrada antes de publicar.';
      case 'EVENT_NOT_FOUND':
        return 'El evento ya no existe.';
      default:
        break;
    }
    if (error.isForbidden) return 'No tenés permiso para hacer esto sobre este evento.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudo completar la acción.';
  }
  return 'No se pudo completar la acción. Verificá tu conexión.';
}

function controlsErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver los Controles de este evento.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudieron cargar los Controles asignados.';
  }
  return 'No se pudieron cargar los Controles asignados. Verificá tu conexión.';
}

/** Detalle de un evento propio (API-MVP 1) con acciones de ciclo de vida y la sección de Control (API-MVP 3/5). */
export default function OrganizerEventDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { hasAccion } = useAuth();
  const puedeGestionarControl = hasAccion(ACCIONES.CONTROL_CREAR);

  const [event, setEvent] = useState<EventResponse | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);

  const [confirmAction, setConfirmAction] = useState<ConfirmActionKind>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const [controls, setControls] = useState<ControlAsignadoResponse[]>([]);
  const [controlsState, setControlsState] = useState<LoadState>('loading');
  const [controlsErrorMsg, setControlsErrorMsg] = useState<string | null>(null);

  const loadEvent = useCallback(async () => {
    if (!id) return;
    setLoadState('loading');
    try {
      const data = await eventService.getOwnedById(id);
      setEvent(data);
      setLoadState('ready');
    } catch (err) {
      setLoadErrorMsg(loadErrorMessage(err));
      setLoadState('error');
    }
  }, [id]);

  const loadControls = useCallback(async () => {
    if (!id) return;
    setControlsState('loading');
    try {
      const data = await controlAsignacionService.getEventControls(id);
      setControls(data);
      setControlsState('ready');
    } catch (err) {
      setControlsErrorMsg(controlsErrorMessage(err));
      setControlsState('error');
    }
  }, [id]);

  useEffect(() => {
    loadEvent();
  }, [loadEvent]);

  useEffect(() => {
    if (puedeGestionarControl) loadControls();
  }, [puedeGestionarControl, loadControls]);

  const handleConfirmAction = async () => {
    if (!confirmAction || !id) return;
    setActionLoading(true);
    setActionError(null);
    try {
      if (confirmAction === 'publish') {
        await eventService.publish(id);
      } else {
        await eventService.cancel(id);
      }
      setConfirmAction(null);
      await loadEvent();
    } catch (err) {
      setActionError(actionErrorMessage(err));
    } finally {
      setActionLoading(false);
    }
  };

  if (loadState === 'loading') {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ORGANIZACIÓN" title="Evento" showBack />
        <AsyncStateView variant="loading" message="Cargando" />
      </View>
    );
  }

  if (loadState === 'error' || !event) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ORGANIZACIÓN" title="Evento" showBack />
        <AsyncStateView variant="error" message={loadErrorMsg ?? 'No se pudo cargar el evento.'} onRetry={loadEvent} />
      </View>
    );
  }

  const puedeEditar = event.estado === 'Borrador';
  const puedePublicar = event.estado === 'Borrador';
  const puedeCancelar = event.estado === 'Borrador' || event.estado === 'Publicado';
  const controlDisponible = event.estado !== 'Cancelado' && event.estado !== 'Finalizado';

  const goToEdit = () => router.push({ pathname: '/organizer/[id]/edit', params: { id } } as Href);
  const goToCreateControl = () => router.push({ pathname: '/organizer/[id]/control-new', params: { id } } as Href);
  const goToAssignControl = () => router.push({ pathname: '/organizer/[id]/control-assign', params: { id } } as Href);

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ORGANIZACIÓN" title={event.nombre} onRefresh={loadEvent} showBack />
      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.topRow}>
          <Text style={styles.categoriaEyebrow}>{event.categoria}</Text>
          <StatusStamp label={event.estado} tone={eventEstadoTone(event.estado)} />
        </View>

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

        <SectionDivider index="01" label="Entradas" />
        {event.ticketGroups.length === 0 ? (
          <Text style={styles.hintText}>Este evento todavía no tiene tipos de entrada cargados.</Text>
        ) : (
          <View style={styles.ticketList}>
            {event.ticketGroups.map((tg) => (
              <View key={tg.id} style={styles.ticketRow}>
                <View style={styles.ticketRowInfo}>
                  <Text style={styles.ticketRowName}>{tg.nombre}</Text>
                  <Text style={styles.ticketRowStock}>{tg.cantidadDisponible} disponibles</Text>
                </View>
                <Text style={styles.ticketRowPrice}>{formatPrecio(tg.precio)}</Text>
              </View>
            ))}
          </View>
        )}

        <SectionDivider index="02" label="Acciones" />
        <View style={styles.actionsRow}>
          {puedeEditar ? <ActionButton label="Editar" variant="secondary" onPress={goToEdit} /> : null}
          {puedePublicar ? <ActionButton label="Publicar" onPress={() => setConfirmAction('publish')} /> : null}
          {puedeCancelar ? (
            <ActionButton label="Cancelar evento" variant="danger" onPress={() => setConfirmAction('cancel')} />
          ) : null}
        </View>
        {!puedeEditar && !puedePublicar && !puedeCancelar ? (
          <Text style={styles.hintText}>Este evento ya no admite más acciones.</Text>
        ) : null}
        {actionError ? <Text style={styles.errorText}>{actionError}</Text> : null}

        {puedeGestionarControl ? (
          <>
            <SectionDivider index="03" label="Control" />
            {controlsState === 'loading' ? (
              <AsyncStateView variant="loading" message="Cargando" />
            ) : controlsState === 'error' ? (
              <AsyncStateView variant="error" message={controlsErrorMsg ?? ''} onRetry={loadControls} />
            ) : controls.length === 0 ? (
              <Text style={styles.hintText}>Todavía no asignaste ningún Control a este evento.</Text>
            ) : (
              <View style={styles.controlList}>
                {controls.map((c) => (
                  <View key={c.controlPersonaId} style={styles.controlRow}>
                    <Text style={styles.controlName}>{c.userName}</Text>
                    <StatusStamp label={c.activo ? 'Activo' : 'Inactivo'} tone={c.activo ? 'success' : 'error'} />
                  </View>
                ))}
              </View>
            )}

            <View style={styles.actionsRow}>
              <ActionButton
                label="Crear Control nuevo"
                variant="secondary"
                onPress={goToCreateControl}
                disabled={!controlDisponible}
              />
              <ActionButton
                label="Asignar Control existente"
                variant="secondary"
                onPress={goToAssignControl}
                disabled={!controlDisponible}
              />
            </View>
            {!controlDisponible ? (
              <Text style={styles.hintText}>Este evento ya no admite asignar Control (cancelado o finalizado).</Text>
            ) : null}
          </>
        ) : null}
      </ScrollView>

      <ConfirmDialog
        visible={confirmAction !== null}
        title={confirmAction === 'publish' ? 'Publicar evento' : 'Cancelar evento'}
        message={
          confirmAction === 'publish'
            ? 'Una vez publicado, el evento queda visible en la cartelera y no se puede volver a Borrador.'
            : 'Esta acción no se puede deshacer. El evento dejará de aceptar compras y validaciones.'
        }
        confirmLabel={confirmAction === 'publish' ? 'Publicar' : 'Sí, cancelar'}
        variant={confirmAction === 'cancel' ? 'danger' : 'primary'}
        loading={actionLoading}
        onConfirm={handleConfirmAction}
        onCancel={() => setConfirmAction(null)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  scroll: {
    padding: spacing.lg,
    paddingBottom: spacing.xxl,
  },
  topRow: {
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
  hintText: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginTop: spacing.sm,
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
  actionsRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.xs,
  },
  controlList: {
    gap: spacing.sm,
    marginBottom: spacing.md,
  },
  controlRow: {
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
  controlName: {
    fontFamily: fonts.bold,
    fontSize: 14,
    color: colors.ink,
  },
});
