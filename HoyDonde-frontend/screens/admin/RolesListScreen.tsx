import React, { useCallback, useMemo, useRef, useState } from 'react';
import { FlatList, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import type { Href } from 'expo-router';
import { useFocusEffect } from '@react-navigation/native';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { Surface } from '@/components/ui/Surface';
import { EstadoFilterRow, EstadoFiltro } from '@/components/admin/EstadoFilterRow';
import { RoleCard } from '@/components/admin/RoleCard';
import { SearchField } from '@/components/admin/SearchField';
import { ACCIONES } from '@/constants/acciones';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/apiError';
import { RolResponse, securityAdminService } from '@/services/securityAdminService';
import FormInput from '@/components/FormInput';

const CODIGO_VALIDO = /^[A-Z][A-Z0-9_]*$/;

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver los roles.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudieron cargar los roles.';
  }
  return 'No se pudieron cargar los roles. Verificá tu conexión.';
}

function createErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'ROLE_ALREADY_EXISTS':
        return 'Ya existe un rol con ese código.';
      case 'VALIDATION_ERROR': {
        const first = error.errors ? Object.values(error.errors).flat()[0] : undefined;
        return first ?? error.message;
      }
      default:
        break;
    }
    if (error.isForbidden) return 'No tenés permiso para crear roles.';
    return error.message || 'No se pudo crear el rol.';
  }
  return 'No se pudo crear el rol. Verificá tu conexión.';
}

interface NuevoRolForm {
  codigo: string;
  nombre: string;
  descripcion: string;
}

const INITIAL_NUEVO_ROL: NuevoRolForm = { codigo: '', nombre: '', descripcion: '' };

/** Catálogo administrable de roles (API_Documentation.md §10). Filtro de texto/estado siempre en memoria. */
export default function RolesListScreen() {
  const { hasAccion } = useAuth();
  const puedeCrear = hasAccion(ACCIONES.ROL_CREAR);

  const [roles, setRoles] = useState<RolResponse[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const [texto, setTexto] = useState('');
  const [estado, setEstado] = useState<EstadoFiltro>('todos');

  const [showCreate, setShowCreate] = useState(false);
  const [nuevoRol, setNuevoRol] = useState<NuevoRolForm>(INITIAL_NUEVO_ROL);
  const [codigoError, setCodigoError] = useState<string | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  // Evita una segunda request mientras ya hay una en vuelo (carga inicial, refresh manual o
  // refresh silencioso al recuperar foco pueden solaparse si el usuario navega rápido).
  const loadInFlightRef = useRef(false);
  // Solo pasa a true tras una carga EXITOSA: si la carga inicial falló, todavía no hay nada que
  // proteger y un reintento por foco debe comportarse como inicial (loading + error visibles).
  const hasLoadedOnceRef = useRef(false);

  const load = useCallback(async (mode: 'initial' | 'refresh' | 'background') => {
    if (loadInFlightRef.current) return;
    loadInFlightRef.current = true;

    if (mode === 'initial') setLoadState('loading');
    try {
      const data = await securityAdminService.listRoles();
      setRoles(data);
      setLoadState('ready');
      hasLoadedOnceRef.current = true;
    } catch (err) {
      // Un refresh silencioso (background) nunca reemplaza una lista ya visible por una
      // pantalla de error: se descarta el intento y se conserva lo último mostrado.
      if (mode !== 'background') {
        setLoadErrorMsg(loadErrorMessage(err));
        setLoadState('error');
      }
    } finally {
      setRefreshing(false);
      loadInFlightRef.current = false;
    }
  }, []);

  // Carga al entrar por primera vez y vuelve a consultar automáticamente cada vez que la
  // pantalla recupera el foco (p. ej. al volver del detalle de un rol tras activarlo/
  // desactivarlo), sin depender de que el usuario toque la ruedita. Búsqueda y filtros
  // (texto/estado) no se tocan acá, así que sobreviven al refresh.
  useFocusEffect(
    useCallback(() => {
      load(hasLoadedOnceRef.current ? 'background' : 'initial');
    }, [load])
  );

  const onRefresh = () => {
    setRefreshing(true);
    load('refresh');
  };

  const rolesFiltrados = useMemo(() => {
    const textoNormalizado = texto.trim().toLowerCase();
    return roles.filter((rol) => {
      if (estado === 'activos' && !rol.activo) return false;
      if (estado === 'inactivos' && rol.activo) return false;
      if (!textoNormalizado) return true;
      return (
        rol.nombre.toLowerCase().includes(textoNormalizado) ||
        rol.codigo.toLowerCase().includes(textoNormalizado) ||
        rol.descripcion.toLowerCase().includes(textoNormalizado)
      );
    });
  }, [roles, texto, estado]);

  const openRol = (codigo: string) => {
    router.push({ pathname: '/admin/roles/[codigo]', params: { codigo } } as unknown as Href);
  };

  const handleCodigoChange = (value: string) => {
    const normalizado = value.toUpperCase().replace(/\s+/g, '_');
    setNuevoRol((prev) => ({ ...prev, codigo: normalizado }));
    setCodigoError(null);
  };

  const handleCrear = async () => {
    if (creating) return;
    setCreateError(null);

    if (!nuevoRol.codigo || !CODIGO_VALIDO.test(nuevoRol.codigo)) {
      setCodigoError('Solo mayúsculas, números y guion bajo, empezando con una letra.');
      return;
    }
    if (!nuevoRol.nombre.trim()) {
      setCreateError('El nombre del rol es obligatorio.');
      return;
    }

    setCreating(true);
    try {
      const creado = await securityAdminService.createRol({
        codigo: nuevoRol.codigo,
        nombre: nuevoRol.nombre.trim(),
        descripcion: nuevoRol.descripcion.trim(),
      });
      setRoles((prev) => [...prev, creado]);
      setShowCreate(false);
      setNuevoRol(INITIAL_NUEVO_ROL);
    } catch (err) {
      setCreateError(createErrorMessage(err));
    } finally {
      setCreating(false);
    }
  };

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Roles y acciones" onRefresh={onRefresh} showBack />

      <View style={styles.filters}>
        <View testID="roles-search-row" style={styles.filterRow}>
          <SearchField value={texto} onChangeText={setTexto} placeholder="Buscar por nombre, código o descripción" />
        </View>

        <View testID="roles-estado-row" style={styles.filterRow}>
          <EstadoFilterRow value={estado} onChange={setEstado} />
        </View>

        {puedeCrear ? (
          <View testID="roles-create-row" style={styles.filterRow}>
            <ActionButton
              label={showCreate ? 'Cancelar' : '+ Crear rol'}
              variant="secondary"
              onPress={() => {
                setShowCreate((prev) => !prev);
                setCreateError(null);
                setCodigoError(null);
              }}
            />
          </View>
        ) : null}
      </View>

      {showCreate ? (
        <Surface variant="sand" style={styles.createCard}>
          <FormInput
            label="Código"
            value={nuevoRol.codigo}
            onChangeText={handleCodigoChange}
            placeholder="SOPORTE"
            error={codigoError}
            autoCapitalize="characters"
          />
          <FormInput
            label="Nombre"
            value={nuevoRol.nombre}
            onChangeText={(text) => setNuevoRol((prev) => ({ ...prev, nombre: text }))}
            placeholder="Soporte"
          />
          <FormInput
            label="Descripción"
            value={nuevoRol.descripcion}
            onChangeText={(text) => setNuevoRol((prev) => ({ ...prev, descripcion: text }))}
            placeholder="Qué puede hacer este rol"
          />
          {createError ? <Text style={styles.createError}>{createError}</Text> : null}
          <ActionButton label="Crear rol" onPress={handleCrear} loading={creating} />
        </Surface>
      ) : null}

      {loadState === 'loading' ? (
        <AsyncStateView variant="loading" message="Cargando roles" />
      ) : loadState === 'error' ? (
        <AsyncStateView variant="error" message={loadErrorMsg ?? ''} onRetry={onRefresh} />
      ) : rolesFiltrados.length === 0 ? (
        <AsyncStateView
          variant="empty"
          icon="badge"
          message={roles.length === 0 ? 'Todavía no hay roles en el catálogo.' : 'Ningún rol coincide con el filtro.'}
        />
      ) : (
        <FlatList
          testID="roles-list"
          data={rolesFiltrados}
          keyExtractor={(item) => item.codigo}
          renderItem={({ item }) => <RoleCard rol={item} onPress={() => openRol(item.codigo)} />}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.ink} />}
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
  filters: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.md,
    gap: spacing.sm,
  },
  // Cada fila ocupa el ancho disponible completo (nunca un ancho fijo en px): búsqueda, filtros
  // de estado y "+ Crear rol" nunca compiten por espacio en la misma fila, así que ninguno se
  // desborda sin importar el tamaño del dispositivo.
  filterRow: {
    width: '100%',
  },
  createCard: {
    marginHorizontal: spacing.lg,
    marginTop: spacing.md,
  },
  createError: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginBottom: spacing.sm,
  },
  list: {
    padding: spacing.lg,
  },
});
