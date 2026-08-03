import React, { useCallback, useEffect, useState } from 'react';
import { FlatList, StyleSheet, Text, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { StatusStamp } from '@/components/ui/StatusStamp';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { ApiError } from '@/services/apiError';
import { ControlResumenResponse, controlAsignacionService } from '@/services/controlAsignacionService';

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver tus Controles.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudieron cargar tus Controles.';
  }
  return 'No se pudieron cargar tus Controles. Verificá tu conexión.';
}

function assignErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'CONTROL_INVALID':
        return 'Este Control ya no está disponible.';
      case 'CONTROL_FOREIGN':
        return 'Este Control pertenece a otro organizador.';
      case 'EVENT_NOT_AVAILABLE_FOR_CONTROL_ASSIGNMENT':
        return 'Este evento ya no admite asignar Control (cancelado o finalizado).';
      case 'EVENT_NOT_FOUND':
        return 'El evento ya no existe.';
      default:
        break;
    }
    if (error.isForbidden) return 'No tenés permiso para asignar Control a este evento.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudo asignar el Control.';
  }
  return 'No se pudo asignar el Control. Verificá tu conexión.';
}

/**
 * Asignar un Control ya existente (API-MVP 3 + 5): la lista sale de GET /events/organizer/controls,
 * nunca se le pide al usuario copiar un PersonaId a mano. La asignación es idempotente — repetirla
 * no duplica ni es un error, así que un reintento accidental simplemente vuelve a mostrar éxito.
 */
export default function AssignControlScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();

  const [controls, setControls] = useState<ControlResumenResponse[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);

  const [assigningId, setAssigningId] = useState<string | null>(null);
  const [assignedIds, setAssignedIds] = useState<Set<string>>(new Set());
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoadState('loading');
    try {
      const data = await controlAsignacionService.getMyControls();
      setControls(data);
      setLoadState('ready');
    } catch (err) {
      setLoadErrorMsg(loadErrorMessage(err));
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const handleAssign = async (controlPersonaId: string) => {
    if (!id || assigningId) return;
    setAssigningId(controlPersonaId);
    setActionError(null);
    try {
      await controlAsignacionService.assignExisting(id, controlPersonaId);
      setAssignedIds((prev) => new Set(prev).add(controlPersonaId));
    } catch (err) {
      setActionError(assignErrorMessage(err));
    } finally {
      setAssigningId(null);
    }
  };

  const renderItem = ({ item }: { item: ControlResumenResponse }) => {
    const isAssigning = assigningId === item.controlPersonaId;
    const isAssigned = assignedIds.has(item.controlPersonaId);

    return (
      <View style={styles.row}>
        <View style={styles.rowInfo}>
          <Text style={styles.rowName}>{item.userName}</Text>
          <StatusStamp label={item.activo ? 'Activo' : 'Inactivo'} tone={item.activo ? 'success' : 'error'} />
        </View>
        {isAssigned ? (
          <StatusStamp label="Asignado" tone="cobalt" />
        ) : (
          <ActionButton
            label="Asignar"
            variant="secondary"
            onPress={() => handleAssign(item.controlPersonaId)}
            loading={isAssigning}
            disabled={assigningId !== null && !isAssigning}
          />
        )}
      </View>
    );
  };

  return (
    <View style={styles.container}>
      <EditorialHeader
        eyebrow="ORGANIZACIÓN"
        title="Asignar Control"
        subtitle="Elegí un Control ya existente para este evento."
        showBack
      />

      {loadState === 'loading' ? (
        <AsyncStateView variant="loading" message="Cargando" />
      ) : loadState === 'error' ? (
        <AsyncStateView variant="error" message={loadErrorMsg ?? ''} onRetry={load} />
      ) : controls.length === 0 ? (
        <AsyncStateView
          variant="empty"
          icon="badge"
          message="Todavía no creaste ningún Control."
          hint="Creá uno nuevo desde el evento."
        />
      ) : (
        <FlatList
          testID="assignable-controls-list"
          data={controls}
          keyExtractor={(item) => item.controlPersonaId}
          renderItem={renderItem}
          contentContainerStyle={styles.list}
        />
      )}

      {actionError ? <Text style={styles.errorText}>{actionError}</Text> : null}

      <View style={styles.footer}>
        <ActionButton label="Volver al evento" variant="secondary" onPress={() => router.back()} />
      </View>
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
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.sand,
    marginBottom: spacing.sm,
  },
  rowInfo: {
    gap: spacing.xs,
    flexShrink: 1,
  },
  rowName: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    textAlign: 'center',
    marginHorizontal: spacing.lg,
  },
  footer: {
    padding: spacing.lg,
  },
});
