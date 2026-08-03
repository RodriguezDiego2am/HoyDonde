import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { useLocalSearchParams } from 'expo-router';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { StatusStamp } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { ACCIONES } from '@/constants/acciones';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/apiError';
import {
  AccionResponse,
  PermisosEfectivosResponse,
  RolResponse,
  securityAdminService,
} from '@/services/securityAdminService';

const ROL_ADMINISTRADOR = 'ADMINISTRADOR';

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'USER_NOT_FOUND') return 'Este usuario ya no existe.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver este usuario.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudo cargar el usuario.';
  }
  return 'No se pudo cargar el usuario. Verificá tu conexión.';
}

function rolActionErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'LAST_ADMINISTRATOR') {
      return 'No se puede quitar: el sistema quedaría sin ningún Administrador efectivo.';
    }
    if (error.code === 'ROLE_NOT_FOUND') return 'Este rol ya no existe.';
    if (error.code === 'USER_NOT_FOUND') return 'Este usuario ya no existe.';
    if (error.isForbidden) return 'No tenés permiso para modificar los roles de este usuario.';
    return error.message || 'No se pudo actualizar el rol.';
  }
  return 'No se pudo actualizar el rol. Verificá tu conexión.';
}

function estadoErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'LAST_ADMINISTRATOR') {
      return 'No se puede desactivar: el sistema quedaría sin ningún Administrador efectivo.';
    }
    if (error.code === 'USER_NOT_FOUND') return 'Este usuario ya no existe.';
    if (error.isForbidden) return 'No tenés permiso para activar/desactivar usuarios.';
    return error.message || 'No se pudo actualizar el estado del usuario.';
  }
  return 'No se pudo actualizar el estado del usuario. Verificá tu conexión.';
}

/**
 * Detalle de un Usuario (API_Documentation.md §10): roles activos, permisos efectivos resueltos
 * en vivo, y sus mutaciones (asignar/quitar rol, activar/desactivar). Nunca muestra UsuarioId,
 * PersonaId ni ningún identificador del proveedor de identidad — usuarioId solo viaja en la URL
 * para armar los requests.
 */
export default function UsuarioDetailScreen() {
  const { usuarioId } = useLocalSearchParams<{ usuarioId: string }>();
  const { hasAccion } = useAuth();

  const puedeAsignarRol = hasAccion(ACCIONES.USUARIO_ASIGNAR_ROL);
  const puedeQuitarRol = hasAccion(ACCIONES.USUARIO_QUITAR_ROL);
  const puedeDesactivar = hasAccion(ACCIONES.USUARIO_DESACTIVAR);
  const puedeVerRolesCatalogo = hasAccion(ACCIONES.ROL_EDITAR);
  const puedeVerAccionesCatalogo = hasAccion(ACCIONES.ROL_ASIGNAR_ACCION);

  const [email, setEmail] = useState<string | null>(null);
  const [permisos, setPermisos] = useState<PermisosEfectivosResponse | null>(null);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);

  const [rolesCatalogo, setRolesCatalogo] = useState<RolResponse[] | null>(null);
  const [accionesCatalogo, setAccionesCatalogo] = useState<AccionResponse[] | null>(null);

  const [rolLoadingCodigo, setRolLoadingCodigo] = useState<string | null>(null);
  const [confirmQuitarRol, setConfirmQuitarRol] = useState<string | null>(null);
  const [rolActionError, setRolActionError] = useState<string | null>(null);

  const [confirmToggleEstado, setConfirmToggleEstado] = useState(false);
  const [togglingEstado, setTogglingEstado] = useState(false);
  const [estadoError, setEstadoError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!usuarioId) return;
    setLoadState('loading');
    try {
      const [lista, permisosData] = await Promise.all([
        securityAdminService.listUsuarios(),
        securityAdminService.getPermisosEfectivos(usuarioId),
      ]);
      const resumen = lista.find((u) => u.usuarioId === usuarioId);
      if (!resumen) {
        throw new ApiError({ code: 'USER_NOT_FOUND', message: 'No existe un usuario con ese id.', traceId: '' }, 404);
      }
      setEmail(resumen.email);
      setPermisos(permisosData);
      setLoadState('ready');
    } catch (err) {
      setLoadErrorMsg(loadErrorMessage(err));
      setLoadState('error');
    }
  }, [usuarioId]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (!puedeVerRolesCatalogo) return;
    let cancelled = false;
    securityAdminService
      .listRoles()
      .then((data) => {
        if (!cancelled) setRolesCatalogo(data);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [puedeVerRolesCatalogo]);

  useEffect(() => {
    if (!puedeVerAccionesCatalogo) return;
    let cancelled = false;
    securityAdminService
      .listAcciones()
      .then((data) => {
        if (!cancelled) setAccionesCatalogo(data);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [puedeVerAccionesCatalogo]);

  const nombreLegibleAccion = useMemo(() => {
    const mapa = new Map<string, string>();
    accionesCatalogo?.forEach((accion) => mapa.set(accion.codigo, accion.descripcion));
    return mapa;
  }, [accionesCatalogo]);

  const refreshPermisos = useCallback(async () => {
    if (!usuarioId) return;
    const permisosData = await securityAdminService.getPermisosEfectivos(usuarioId);
    setPermisos(permisosData);
  }, [usuarioId]);

  const rolesAsignables = useMemo(() => {
    if (!rolesCatalogo || !permisos) return [];
    return rolesCatalogo.filter((rol) => !permisos.roles.includes(rol.codigo));
  }, [rolesCatalogo, permisos]);

  const handleAsignarRol = async (rolCodigo: string) => {
    if (!usuarioId || rolLoadingCodigo) return;
    setRolLoadingCodigo(rolCodigo);
    setRolActionError(null);
    try {
      await securityAdminService.asignarRol(usuarioId, rolCodigo);
      await refreshPermisos();
    } catch (err) {
      setRolActionError(rolActionErrorMessage(err));
    } finally {
      setRolLoadingCodigo(null);
    }
  };

  const ejecutarQuitarRol = async (rolCodigo: string) => {
    if (!usuarioId) return;
    setRolLoadingCodigo(rolCodigo);
    setRolActionError(null);
    try {
      await securityAdminService.quitarRol(usuarioId, rolCodigo);
      await refreshPermisos();
    } catch (err) {
      setRolActionError(rolActionErrorMessage(err));
    } finally {
      setRolLoadingCodigo(null);
      setConfirmQuitarRol(null);
    }
  };

  const handleToggleEstado = async () => {
    if (!usuarioId || !permisos) return;
    setTogglingEstado(true);
    setEstadoError(null);
    const proximoEstado = !permisos.usuarioActivo;
    try {
      await securityAdminService.setUsuarioActivo(usuarioId, proximoEstado);
      setPermisos((prev) => (prev ? { ...prev, usuarioActivo: proximoEstado } : prev));
    } catch (err) {
      setEstadoError(estadoErrorMessage(err));
    } finally {
      setTogglingEstado(false);
      setConfirmToggleEstado(false);
    }
  };

  if (loadState === 'loading') {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="USUARIO" title="Cargando" showBack />
        <AsyncStateView variant="loading" message="Cargando usuario" />
      </View>
    );
  }

  if (loadState === 'error' || !permisos) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="USUARIO" title="No disponible" showBack />
        <AsyncStateView variant="error" message={loadErrorMsg ?? ''} onRetry={load} />
      </View>
    );
  }

  const esAdministrador = permisos.roles.includes(ROL_ADMINISTRADOR);

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="USUARIO" title={email ?? ''} showBack />
      <ScrollView contentContainerStyle={styles.scroll}>
        <Surface variant="sand" style={styles.estadoCard}>
          <StatusStamp label={permisos.usuarioActivo ? 'Activo' : 'Inactivo'} tone={permisos.usuarioActivo ? 'success' : 'error'} />
          {puedeDesactivar ? (
            <ActionButton
              label={permisos.usuarioActivo ? 'Desactivar cuenta' : 'Activar cuenta'}
              variant={permisos.usuarioActivo ? 'danger' : 'secondary'}
              onPress={() => (permisos.usuarioActivo ? setConfirmToggleEstado(true) : handleToggleEstado())}
              loading={togglingEstado && permisos.usuarioActivo === false}
            />
          ) : null}
        </Surface>
        {estadoError ? <Text style={styles.errorText}>{estadoError}</Text> : null}

        <SectionDivider index="01" label="Roles activos" />
        {permisos.roles.length === 0 ? (
          <Text style={styles.emptyText}>Sin roles asignados.</Text>
        ) : (
          <View style={styles.rolesList}>
            {permisos.roles.map((rolCodigo) => (
              <View key={rolCodigo} style={styles.rolRow}>
                <StatusStamp label={rolCodigo} tone="cobalt" />
                {puedeQuitarRol ? (
                  <ActionButton
                    label="Quitar"
                    variant="ghost"
                    onPress={() => setConfirmQuitarRol(rolCodigo)}
                    loading={rolLoadingCodigo === rolCodigo}
                  />
                ) : null}
              </View>
            ))}
          </View>
        )}

        {puedeAsignarRol && rolesAsignables.length > 0 ? (
          <>
            <Text style={styles.subLabel}>Asignar otro rol</Text>
            <View style={styles.rolesList}>
              {rolesAsignables.map((rol) => (
                <View key={rol.codigo} style={styles.rolAsignableRow}>
                  <View style={styles.rolAsignableInfo}>
                    <Text style={styles.rolAsignableNombre}>{rol.nombre}</Text>
                    <Text style={styles.rolAsignableCodigo}>{rol.codigo}</Text>
                  </View>
                  <ActionButton
                    label="Asignar"
                    variant="secondary"
                    onPress={() => handleAsignarRol(rol.codigo)}
                    loading={rolLoadingCodigo === rol.codigo}
                    disabled={rolLoadingCodigo !== null && rolLoadingCodigo !== rol.codigo}
                  />
                </View>
              ))}
            </View>
          </>
        ) : null}

        {rolActionError ? <Text style={styles.errorText}>{rolActionError}</Text> : null}

        <SectionDivider index="02" label={`Permisos efectivos (${permisos.acciones.length})`} />
        {permisos.acciones.length === 0 ? (
          <Text style={styles.emptyText}>Sin acciones habilitadas.</Text>
        ) : (
          <View style={styles.accionesList}>
            {permisos.acciones.map((accionCodigo) => (
              <View key={accionCodigo} style={styles.accionRow}>
                <Text style={styles.accionNombre}>{nombreLegibleAccion.get(accionCodigo) ?? accionCodigo}</Text>
                <Text style={styles.accionCodigo}>{accionCodigo}</Text>
              </View>
            ))}
          </View>
        )}
      </ScrollView>

      <ConfirmDialog
        visible={confirmQuitarRol !== null}
        title="Quitar rol"
        message={
          confirmQuitarRol === ROL_ADMINISTRADOR
            ? 'Este usuario perderá el rol Administrador. Si es la última cuenta con Administrador efectivo, la API va a rechazar la operación.'
            : `Este usuario perderá el rol "${confirmQuitarRol ?? ''}" y las acciones que solo obtiene a través de él.`
        }
        confirmLabel="Sí, quitar"
        variant="danger"
        loading={rolLoadingCodigo === confirmQuitarRol}
        onConfirm={() => confirmQuitarRol && ejecutarQuitarRol(confirmQuitarRol)}
        onCancel={() => setConfirmQuitarRol(null)}
      />

      <ConfirmDialog
        visible={confirmToggleEstado}
        title="Desactivar cuenta"
        message={
          esAdministrador
            ? 'Este usuario tiene el rol Administrador. Si es la última cuenta con Administrador efectivo, la API va a rechazar la operación.'
            : 'El usuario no va a poder iniciar sesión hasta que se reactive la cuenta.'
        }
        confirmLabel="Desactivar"
        variant="danger"
        loading={togglingEstado}
        onConfirm={handleToggleEstado}
        onCancel={() => setConfirmToggleEstado(false)}
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
    // El label del botón cambia ("Activar cuenta"/"Desactivar cuenta"): envolver evita que se
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
  emptyText: {
    fontFamily: fonts.regular,
    fontSize: 14,
    color: colors.inkSoft,
  },
  rolesList: {
    gap: spacing.sm,
  },
  rolRow: {
    flexDirection: 'row',
    // Los códigos de rol son administrables y pueden ser largos: envolver evita que el botón
    // "Quitar" se desborde en vez de asumir un ancho de dispositivo.
    flexWrap: 'wrap',
    justifyContent: 'space-between',
    alignItems: 'center',
    rowGap: spacing.xs,
  },
  subLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.inkSoft,
    textTransform: 'uppercase',
    marginTop: spacing.lg,
    marginBottom: spacing.sm,
  },
  rolAsignableRow: {
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
  rolAsignableInfo: {
    flexShrink: 1,
  },
  rolAsignableNombre: {
    fontFamily: fonts.bold,
    fontSize: 14,
    color: colors.ink,
  },
  rolAsignableCodigo: {
    fontFamily: fonts.medium,
    fontSize: 11,
    color: colors.inkSoft,
    marginTop: 2,
  },
  accionesList: {
    gap: spacing.xs,
  },
  accionRow: {
    borderBottomWidth: 1,
    borderBottomColor: colors.sand,
    paddingVertical: spacing.xs,
  },
  accionNombre: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.ink,
  },
  accionCodigo: {
    fontFamily: fonts.regular,
    fontSize: 11,
    color: colors.inkSoft,
    marginTop: 1,
  },
});
