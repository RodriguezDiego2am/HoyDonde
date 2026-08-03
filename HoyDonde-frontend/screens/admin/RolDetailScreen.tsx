import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { StatusStamp } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { AccionChip } from '@/components/admin/AccionChip';
import { EstadoFilterRow, EstadoFiltro } from '@/components/admin/EstadoFilterRow';
import { SearchField } from '@/components/admin/SearchField';
import { ACCIONES } from '@/constants/acciones';
import { borderWidth, colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/apiError';
import { AccionResponse, RolResponse, securityAdminService } from '@/services/securityAdminService';
import { ORDEN_DOMINIOS, dominioDeAccion } from '@/utils/accionDominio';
import FormInput from '@/components/FormInput';

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'ROLE_NOT_FOUND') return 'Este rol ya no existe.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver este rol.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudo cargar el rol.';
  }
  return 'No se pudo cargar el rol. Verificá tu conexión.';
}

function editErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'ROLE_NOT_FOUND') return 'Este rol ya no existe.';
    if (error.code === 'VALIDATION_ERROR') {
      const first = error.errors ? Object.values(error.errors).flat()[0] : undefined;
      return first ?? error.message;
    }
    if (error.isForbidden) return 'No tenés permiso para editar este rol.';
    return error.message || 'No se pudieron guardar los cambios.';
  }
  return 'No se pudieron guardar los cambios. Verificá tu conexión.';
}

function estadoErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'LAST_ADMINISTRATOR') {
      return 'No se puede desactivar: el sistema quedaría sin ningún Administrador efectivo.';
    }
    if (error.code === 'ROLE_NOT_FOUND') return 'Este rol ya no existe.';
    if (error.isForbidden) return 'No tenés permiso para activar/desactivar roles.';
    return error.message || 'No se pudo actualizar el estado del rol.';
  }
  return 'No se pudo actualizar el estado del rol. Verificá tu conexión.';
}

function accionErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'ACTION_NOT_FOUND') return 'Esta acción ya no existe en el catálogo.';
    if (error.code === 'ROLE_NOT_FOUND') return 'Este rol ya no existe.';
    if (error.isForbidden) return 'No tenés permiso para modificar las acciones de este rol.';
    return error.message || 'No se pudo actualizar la acción.';
  }
  return 'No se pudo actualizar la acción. Verificá tu conexión.';
}

function eliminarErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'ROLE_NOT_FOUND') return 'Este rol ya no existe.';
    if (error.code === 'ROL_PROTEGIDO') return 'Este rol es esencial y no puede eliminarse físicamente.';
    if (error.code === 'ROL_DEBE_ESTAR_INACTIVO') return 'El rol debe estar inactivo antes de poder eliminarse.';
    if (error.code === 'ROL_TIENE_USUARIOS_ASIGNADOS') return 'El rol tiene usuarios asignados y no puede eliminarse.';
    if (error.isForbidden) return 'No tenés permiso para eliminar este rol.';
    return error.message || 'No se pudo eliminar el rol.';
  }
  return 'No se pudo eliminar el rol. Verificá tu conexión.';
}

/** Los 4 roles base sembrados por SecurityCatalogSeeder (espejo informativo de
 * HoyDonde.API/Repositories/RolesEsenciales.cs): nunca pueden eliminarse físicamente. Solo se usa
 * para mostrar una nota informativa sobre el rol objetivo, nunca para gatear por el rol del
 * usuario autenticado -eso siempre se decide con hasAccion(ACCIONES.*)-. */
const ROLES_ESENCIALES = ['ADMINISTRADOR', 'ORGANIZADOR', 'CLIENTE', 'CONTROL'];

/**
 * Detalle de un Rol (API_Documentation.md §10): edición de nombre/descripción, activar/desactivar
 * y administración de qué acciones tiene asignadas. El código del rol es la identidad del
 * documento en Firestore y nunca se ofrece como editable.
 */
export default function RolDetailScreen() {
  const { codigo } = useLocalSearchParams<{ codigo: string }>();
  const { hasAccion } = useAuth();

  const puedeEditar = hasAccion(ACCIONES.ROL_EDITAR);
  const puedeActivar = hasAccion(ACCIONES.ROL_ACTIVAR);
  const puedeAsignar = hasAccion(ACCIONES.ROL_ASIGNAR_ACCION);
  const puedeQuitar = hasAccion(ACCIONES.ROL_QUITAR_ACCION);
  const puedeEliminar = hasAccion(ACCIONES.ROL_ELIMINAR);

  const [rol, setRol] = useState<RolResponse | null>(null);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);

  const [nombre, setNombre] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [savingEdit, setSavingEdit] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  const [confirmDesactivar, setConfirmDesactivar] = useState(false);
  const [togglingEstado, setTogglingEstado] = useState(false);
  const [estadoError, setEstadoError] = useState<string | null>(null);

  const [confirmEliminar, setConfirmEliminar] = useState(false);
  const [eliminando, setEliminando] = useState(false);
  const [eliminarError, setEliminarError] = useState<string | null>(null);

  const [catalogo, setCatalogo] = useState<AccionResponse[] | null>(null);
  const [catalogoError, setCatalogoError] = useState<string | null>(null);
  const [accionTexto, setAccionTexto] = useState('');
  const [accionEstado, setAccionEstado] = useState<EstadoFiltro>('todos');
  const [accionLoadingCodigo, setAccionLoadingCodigo] = useState<string | null>(null);
  const [confirmQuitarCodigo, setConfirmQuitarCodigo] = useState<string | null>(null);
  const [accionActionError, setAccionActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!codigo) return;
    setLoadState('loading');
    try {
      const data = await securityAdminService.getRol(codigo);
      setRol(data);
      setNombre(data.nombre);
      setDescripcion(data.descripcion);
      setLoadState('ready');
    } catch (err) {
      setLoadErrorMsg(loadErrorMessage(err));
      setLoadState('error');
    }
  }, [codigo]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (!puedeAsignar) return;
    let cancelled = false;
    securityAdminService
      .listAcciones()
      .then((data) => {
        if (!cancelled) setCatalogo(data);
      })
      .catch(() => {
        if (!cancelled) setCatalogoError('No se pudo cargar el catálogo completo de acciones.');
      });
    return () => {
      cancelled = true;
    };
  }, [puedeAsignar]);

  const dirty = rol !== null && (nombre !== rol.nombre || descripcion !== rol.descripcion);

  const handleGuardarEdicion = async () => {
    if (savingEdit || !codigo) return;
    if (!nombre.trim()) {
      setEditError('El nombre del rol es obligatorio.');
      return;
    }
    setSavingEdit(true);
    setEditError(null);
    try {
      const actualizado = await securityAdminService.updateRol(codigo, {
        nombre: nombre.trim(),
        descripcion: descripcion.trim(),
      });
      setRol(actualizado);
      setNombre(actualizado.nombre);
      setDescripcion(actualizado.descripcion);
    } catch (err) {
      setEditError(editErrorMessage(err));
    } finally {
      setSavingEdit(false);
    }
  };

  const toggleEstado = async (activo: boolean) => {
    if (!codigo || togglingEstado) return;
    setTogglingEstado(true);
    setEstadoError(null);
    try {
      await securityAdminService.setRolActivo(codigo, activo);
      setRol((prev) => (prev ? { ...prev, activo } : prev));
    } catch (err) {
      setEstadoError(estadoErrorMessage(err));
    } finally {
      setTogglingEstado(false);
      setConfirmDesactivar(false);
    }
  };

  const asignarAccion = async (accionCodigo: string) => {
    if (!codigo || accionLoadingCodigo) return;
    setAccionLoadingCodigo(accionCodigo);
    setAccionActionError(null);
    try {
      await securityAdminService.asignarAccion(codigo, accionCodigo);
      setRol((prev) =>
        prev && !prev.acciones.includes(accionCodigo) ? { ...prev, acciones: [...prev.acciones, accionCodigo] } : prev
      );
    } catch (err) {
      setAccionActionError(accionErrorMessage(err));
    } finally {
      setAccionLoadingCodigo(null);
    }
  };

  const handleEliminar = async () => {
    if (!codigo || eliminando) return;
    setEliminando(true);
    setEliminarError(null);
    try {
      await securityAdminService.deleteRol(codigo);
      router.replace('/admin/roles');
    } catch (err) {
      setEliminarError(eliminarErrorMessage(err));
      setEliminando(false);
      setConfirmEliminar(false);
    }
  };

  const ejecutarQuitarAccion = async (accionCodigo: string) => {
    if (!codigo) return;
    setAccionLoadingCodigo(accionCodigo);
    setAccionActionError(null);
    try {
      await securityAdminService.quitarAccion(codigo, accionCodigo);
      setRol((prev) => (prev ? { ...prev, acciones: prev.acciones.filter((c) => c !== accionCodigo) } : prev));
    } catch (err) {
      setAccionActionError(accionErrorMessage(err));
    } finally {
      setAccionLoadingCodigo(null);
      setConfirmQuitarCodigo(null);
    }
  };

  const seccionesPorDominio = useMemo(() => {
    if (!catalogo || !rol) return [];
    const textoNormalizado = accionTexto.trim().toLowerCase();
    const filtradas = catalogo.filter((accion) => {
      if (accionEstado === 'activos' && !accion.activo) return false;
      if (accionEstado === 'inactivos' && accion.activo) return false;
      if (!textoNormalizado) return true;
      return (
        accion.descripcion.toLowerCase().includes(textoNormalizado) ||
        accion.codigo.toLowerCase().includes(textoNormalizado)
      );
    });

    const porDominio = new Map<string, AccionResponse[]>();
    for (const accion of filtradas) {
      const dominio = dominioDeAccion(accion.codigo);
      const lista = porDominio.get(dominio) ?? [];
      lista.push(accion);
      porDominio.set(dominio, lista);
    }

    return ORDEN_DOMINIOS.filter((dominio) => porDominio.has(dominio)).map((dominio) => ({
      dominio,
      acciones: porDominio.get(dominio)!,
    }));
  }, [catalogo, rol, accionTexto, accionEstado]);

  if (loadState === 'loading') {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ROL" title="Cargando" showBack />
        <AsyncStateView variant="loading" message="Cargando rol" />
      </View>
    );
  }

  if (loadState === 'error' || !rol) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ROL" title="No disponible" showBack />
        <AsyncStateView variant="error" message={loadErrorMsg ?? ''} onRetry={load} />
      </View>
    );
  }

  const esEsencial = ROLES_ESENCIALES.includes(rol.codigo);

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ROL" title={rol.nombre} subtitle={rol.codigo} showBack />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <Surface variant="sand" style={styles.estadoCard}>
          <StatusStamp label={rol.activo ? 'Activo' : 'Inactivo'} tone={rol.activo ? 'success' : 'error'} />
          {puedeActivar ? (
            <ActionButton
              label={rol.activo ? 'Dar de baja lógica' : 'Reactivar rol'}
              variant={rol.activo ? 'danger' : 'secondary'}
              onPress={() => (rol.activo ? setConfirmDesactivar(true) : toggleEstado(true))}
              loading={togglingEstado && rol.activo === false}
            />
          ) : null}
        </Surface>
        {puedeActivar ? (
          <Text style={styles.explicacionBaja}>
            La baja lógica conserva el historial y las asignaciones del rol: solo deja de otorgar
            permisos efectivos, y puede reactivarse en cualquier momento.
          </Text>
        ) : null}
        {estadoError ? <Text style={styles.errorText}>{estadoError}</Text> : null}

        {puedeEditar ? (
          <>
            <SectionDivider index="01" label="Datos del rol" />
            <FormInput label="Nombre" value={nombre} onChangeText={setNombre} placeholder="Nombre del rol" />
            <FormInput
              label="Descripción"
              value={descripcion}
              onChangeText={setDescripcion}
              placeholder="Qué puede hacer este rol"
            />
            {editError ? <Text style={styles.errorText}>{editError}</Text> : null}
            <ActionButton
              label="Guardar cambios"
              variant="secondary"
              onPress={handleGuardarEdicion}
              loading={savingEdit}
              disabled={!dirty}
            />
          </>
        ) : rol.descripcion ? (
          <Text style={styles.descripcionSolaLectura}>{rol.descripcion}</Text>
        ) : null}

        <SectionDivider index="02" label={`Acciones (${rol.acciones.length})`} />

        {puedeAsignar ? (
          <>
            <SearchField value={accionTexto} onChangeText={setAccionTexto} placeholder="Buscar acción" />
            <View style={styles.accionFiltroFooter}>
              <EstadoFilterRow value={accionEstado} onChange={setAccionEstado} />
            </View>

            {catalogoError ? (
              <Text style={styles.errorText}>{catalogoError}</Text>
            ) : !catalogo ? (
              <AsyncStateView variant="loading" message="Cargando catálogo de acciones" />
            ) : (
              seccionesPorDominio.map((seccion) => (
                <View key={seccion.dominio} style={styles.dominioBlock}>
                  <Text style={styles.dominioLabel}>{seccion.dominio.toUpperCase()}</Text>
                  {seccion.acciones.map((accion) => {
                    const assigned = rol.acciones.includes(accion.codigo);
                    return (
                      <AccionChip
                        key={accion.codigo}
                        nombre={accion.descripcion}
                        codigo={accion.codigo}
                        assigned={assigned}
                        accionInactiva={!accion.activo}
                        loading={accionLoadingCodigo === accion.codigo}
                        disabled={
                          (assigned && !puedeQuitar) ||
                          (!assigned && !puedeAsignar) ||
                          (accionLoadingCodigo !== null && accionLoadingCodigo !== accion.codigo)
                        }
                        onPress={() =>
                          assigned ? setConfirmQuitarCodigo(accion.codigo) : asignarAccion(accion.codigo)
                        }
                      />
                    );
                  })}
                </View>
              ))
            )}
          </>
        ) : rol.acciones.length > 0 ? (
          <View style={styles.readOnlyAccionesRow}>
            {rol.acciones.map((accionCodigo) => (
              <StatusStamp key={accionCodigo} label={accionCodigo} tone="cobalt" />
            ))}
          </View>
        ) : (
          <Text style={styles.descripcionSolaLectura}>Este rol no tiene acciones asignadas.</Text>
        )}

        {accionActionError ? <Text style={styles.errorText}>{accionActionError}</Text> : null}

        {puedeEliminar ? (
          <>
            <SectionDivider index="03" label="Zona de peligro" />
            {esEsencial ? (
              <Text style={styles.protegidoNota}>
                Este es un rol esencial del sistema y nunca puede eliminarse físicamente.
              </Text>
            ) : rol.activo ? (
              <Text style={styles.protegidoNota}>
                Para eliminar este rol de forma definitiva, primero dalo de baja lógica.
              </Text>
            ) : (
              <Surface variant="sand" style={styles.dangerCard}>
                <Text style={styles.dangerTitle}>Baja física definitiva</Text>
                <Text style={styles.dangerText}>
                  Borra el rol y todas sus asignaciones de acciones de forma permanente. Esta
                  acción no puede deshacerse. Solo es posible porque el rol está inactivo y no
                  tiene ningún usuario asignado.
                </Text>
                {eliminarError ? <Text style={styles.errorText}>{eliminarError}</Text> : null}
                <ActionButton
                  label="Eliminar definitivamente"
                  variant="danger"
                  onPress={() => setConfirmEliminar(true)}
                  loading={eliminando}
                  disabled={eliminando}
                />
              </Surface>
            )}
          </>
        ) : null}
      </ScrollView>

      <ConfirmDialog
        visible={confirmDesactivar}
        title="Dar de baja lógica"
        message={`Los usuarios con "${rol.nombre}" perderán las acciones que solo obtienen por este rol. El rol y sus asignaciones se conservan y podés reactivarlo cuando quieras. Si esta operación dejaría el sistema sin ningún Administrador efectivo, la API la va a rechazar.`}
        confirmLabel="Dar de baja"
        variant="danger"
        loading={togglingEstado}
        onConfirm={() => toggleEstado(false)}
        onCancel={() => setConfirmDesactivar(false)}
      />

      <ConfirmDialog
        visible={confirmEliminar}
        title="Eliminar rol definitivamente"
        message={`"${rol.nombre}" (${rol.codigo}) y todas sus asignaciones de acciones se van a borrar de forma permanente. Esta acción no puede deshacerse.`}
        confirmLabel="Eliminar definitivamente"
        variant="danger"
        loading={eliminando}
        onConfirm={handleEliminar}
        onCancel={() => setConfirmEliminar(false)}
      />

      <ConfirmDialog
        visible={confirmQuitarCodigo !== null}
        title="Quitar acción"
        message={
          confirmQuitarCodigo
            ? `Los usuarios con "${rol.nombre}" van a perder "${
                catalogo?.find((a) => a.codigo === confirmQuitarCodigo)?.descripcion ?? confirmQuitarCodigo
              }" a través de este rol.`
            : ''
        }
        confirmLabel="Quitar"
        variant="danger"
        loading={accionLoadingCodigo === confirmQuitarCodigo}
        onConfirm={() => confirmQuitarCodigo && ejecutarQuitarAccion(confirmQuitarCodigo)}
        onCancel={() => setConfirmQuitarCodigo(null)}
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
  },
  estadoCard: {
    flexDirection: 'row',
    // El label del botón cambia ("Activar rol"/"Desactivar rol"): envolver evita que se
    // desborde en dispositivos angostos en vez de depender de un ancho fijo.
    flexWrap: 'wrap',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginTop: spacing.sm,
  },
  descripcionSolaLectura: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.ink,
  },
  accionFiltroFooter: {
    marginTop: spacing.sm,
    marginBottom: spacing.sm,
  },
  dominioBlock: {
    marginBottom: spacing.md,
  },
  dominioLabel: {
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 1,
    color: colors.inkSoft,
    marginBottom: spacing.sm,
  },
  readOnlyAccionesRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
  explicacionBaja: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    marginTop: spacing.xs,
  },
  protegidoNota: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    fontStyle: 'italic',
  },
  dangerCard: {
    borderColor: colors.error,
    borderWidth: borderWidth.thick,
  },
  dangerTitle: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.error,
  },
  dangerText: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.ink,
    marginTop: spacing.xs,
    marginBottom: spacing.sm,
    lineHeight: 18,
  },
});
