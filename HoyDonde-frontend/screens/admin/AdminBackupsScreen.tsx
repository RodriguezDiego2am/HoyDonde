import React, { useCallback, useEffect, useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { Surface } from '@/components/ui/Surface';
import { ACCIONES } from '@/constants/acciones';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/APIService';
import { backupService, FirestoreBackupResponse } from '@/services/backupService';
import { formatFechaHora } from '@/utils/format';

function operationLabel(backup: FirestoreBackupResponse): string {
  if (backup.operation === 'Import') return 'Restauración';
  return backup.kind === 'AutomaticPreRestore' ? 'Copia automática previa a restauración' : 'Copia manual de Firestore';
}

function stateLabel(state: FirestoreBackupResponse['state']): string {
  return { Pending: 'Pendiente', Running: 'En curso', Succeeded: 'Completada', Failed: 'Fallida' }[state];
}

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para administrar copias.';
    return error.message || 'No se pudo completar la operación.';
  }
  return 'No se pudo completar la operación. Verificá tu conexión.';
}

/** Manual native Firestore export/import history. Polling happens only when the administrator refreshes this screen. */
export default function AdminBackupsScreen() {
  const { hasAccion } = useAuth();
  const puedeAdministrar = hasAccion(ACCIONES.BACKUP_ADMINISTRAR);
  const [backups, setBackups] = useState<FirestoreBackupResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [mutatingId, setMutatingId] = useState<string | null>(null);
  const [restoreTarget, setRestoreTarget] = useState<FirestoreBackupResponse | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<FirestoreBackupResponse | null>(null);

  const load = useCallback(async () => {
    if (!puedeAdministrar) return;
    setLoading(true);
    setError(null);
    try {
      setBackups(await backupService.list());
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [puedeAdministrar]);

  useEffect(() => {
    load();
  }, [load]);

  const createManual = async () => {
    if (creating) return;
    setCreating(true);
    setError(null);
    try {
      await backupService.createManual();
      await load();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setCreating(false);
    }
  };

  const restore = async () => {
    if (!restoreTarget || mutatingId) return;
    setMutatingId(restoreTarget.id);
    setError(null);
    try {
      await backupService.restore(restoreTarget.id);
      setRestoreTarget(null);
      await load();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setMutatingId(null);
    }
  };

  const remove = async () => {
    if (!deleteTarget || mutatingId) return;
    setMutatingId(deleteTarget.id);
    setError(null);
    try {
      await backupService.delete(deleteTarget.id);
      setDeleteTarget(null);
      await load();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setMutatingId(null);
    }
  };

  if (!puedeAdministrar) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Copias de seguridad" subtitle="Acceso restringido." showBack />
        <AsyncStateView variant="error" message="Tu cuenta no tiene permiso para administrar copias." />
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Copias de seguridad" subtitle="Exportaciones nativas de Firestore." showBack onRefresh={load} />
      <ScrollView contentContainerStyle={styles.scroll}>
        <Surface style={styles.intro}>
          <MaterialIcons name="cloud-upload" size={24} color={colors.ink} />
          <View style={styles.introText}>
            <Text style={styles.introTitle}>Copia manual</Text>
            <Text style={styles.introDescription}>La exportación se ejecuta en Google Cloud. No se serializan documentos desde la aplicación.</Text>
          </View>
        </Surface>
        <ActionButton label="Crear copia manual" onPress={createManual} loading={creating} disabled={creating} />
        <Text style={styles.retention}>Referencia de retención: 7 copias diarias y 4 semanales. No se eliminan automáticamente.</Text>

        <SectionDivider index="01" label="Historial" />
        {loading ? (
          <AsyncStateView variant="loading" message="Consultando el estado de las copias" />
        ) : error ? (
          <AsyncStateView variant="error" message={error} onRetry={load} />
        ) : backups.length === 0 ? (
          <AsyncStateView variant="empty" icon="backup" message="Todavía no hay copias registradas." />
        ) : (
          backups.map((backup) => {
            const terminalExport = backup.operation === 'Export' && (backup.state === 'Succeeded' || backup.state === 'Failed');
            return (
              <Surface key={backup.id} style={styles.backupCard}>
                <View style={styles.cardHeader}>
                  <Text style={styles.cardTitle}>{operationLabel(backup)}</Text>
                  <Text style={[styles.state, backup.state === 'Failed' && styles.stateFailed]}>{stateLabel(backup.state)}</Text>
                </View>
                <Text style={styles.meta}>Solicitada: {formatFechaHora(backup.createdAt)}</Text>
                {backup.completedAt ? <Text style={styles.meta}>Finalizada: {formatFechaHora(backup.completedAt)}</Text> : null}
                {backup.operation === 'Import' && backup.state === 'Pending' ? <Text style={styles.note}>Esperando que finalice la copia automática previa.</Text> : null}
                {backup.operation === 'Export' && backup.state === 'Succeeded' ? (
                  <View style={styles.actions}>
                    <View style={styles.action}><ActionButton label="Restaurar" variant="secondary" onPress={() => setRestoreTarget(backup)} disabled={mutatingId !== null} /></View>
                    <View style={styles.action}><ActionButton label="Eliminar copia" variant="danger" onPress={() => setDeleteTarget(backup)} disabled={mutatingId !== null} /></View>
                  </View>
                ) : terminalExport ? (
                  <View style={styles.actions}><View style={styles.action}><ActionButton label="Eliminar copia" variant="danger" onPress={() => setDeleteTarget(backup)} disabled={mutatingId !== null} /></View></View>
                ) : null}
              </Surface>
            );
          })
        )}
      </ScrollView>

      <ConfirmDialog
        visible={restoreTarget !== null}
        title="Restaurar esta copia"
        message="Antes de importar se creará una copia automática del estado actual. La importación sobrescribe los documentos incluidos en la copia elegida, pero no elimina documentos creados después."
        confirmLabel="Crear copia y restaurar"
        loading={mutatingId === restoreTarget?.id}
        onConfirm={restore}
        onCancel={() => !mutatingId && setRestoreTarget(null)}
      />
      <ConfirmDialog
        visible={deleteTarget !== null}
        title="Eliminar esta copia definitivamente"
        message="Esta acción elimina primero los archivos de la copia en Cloud Storage y, solo si eso termina bien, su registro de historial. La copia deja de existir y no se puede recuperar."
        confirmLabel="Eliminar definitivamente"
        variant="danger"
        loading={mutatingId === deleteTarget?.id}
        onConfirm={remove}
        onCancel={() => !mutatingId && setDeleteTarget(null)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.paper },
  scroll: { padding: spacing.lg, paddingBottom: spacing.xxl },
  intro: { flexDirection: 'row', gap: spacing.md, marginBottom: spacing.md },
  introText: { flex: 1 },
  introTitle: { fontFamily: fonts.bold, color: colors.ink, fontSize: 16 },
  introDescription: { fontFamily: fonts.regular, color: colors.inkSoft, fontSize: 13, marginTop: 3, lineHeight: 18 },
  retention: { fontFamily: fonts.regular, color: colors.inkSoft, fontSize: 12, lineHeight: 17, marginTop: spacing.sm },
  backupCard: { marginBottom: spacing.md },
  cardHeader: { flexDirection: 'row', justifyContent: 'space-between', gap: spacing.sm, marginBottom: spacing.xs },
  cardTitle: { flex: 1, fontFamily: fonts.bold, color: colors.ink, fontSize: 15 },
  state: { fontFamily: fonts.bold, color: colors.inkSoft, fontSize: 12, textTransform: 'uppercase' },
  stateFailed: { color: colors.error },
  meta: { fontFamily: fonts.regular, color: colors.inkSoft, fontSize: 12, marginTop: 2 },
  note: { fontFamily: fonts.medium, color: colors.inkSoft, fontSize: 12, marginTop: spacing.sm },
  actions: { flexDirection: 'row', gap: spacing.sm, marginTop: spacing.md },
  action: { flex: 1 },
});
