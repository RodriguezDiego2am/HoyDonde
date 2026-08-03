import React, { useCallback, useMemo, useRef, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import type { Href } from 'expo-router';
import { useFocusEffect } from '@react-navigation/native';

import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { EstadoFilterRow, EstadoFiltro } from '@/components/admin/EstadoFilterRow';
import { SearchField } from '@/components/admin/SearchField';
import { UserRow } from '@/components/admin/UserRow';
import { colors, fonts, spacing } from '@/constants/theme';
import { ApiError } from '@/services/apiError';
import { UsuarioResumenResponse, securityAdminService } from '@/services/securityAdminService';

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver los usuarios.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudieron cargar los usuarios.';
  }
  return 'No se pudieron cargar los usuarios. Verificá tu conexión.';
}

/** Listado de cuentas (API_Documentation.md §10, GET /security/usuarios). Filtros siempre en memoria. */
export default function UsuariosListScreen() {
  const [usuarios, setUsuarios] = useState<UsuarioResumenResponse[]>([]);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const [texto, setTexto] = useState('');
  const [estado, setEstado] = useState<EstadoFiltro>('todos');
  const [rolFiltro, setRolFiltro] = useState<string | undefined>(undefined);

  // Mismo criterio que RolesListScreen: evita solapar cargas y protege la lista visible de un
  // refresh silencioso fallido en background.
  const loadInFlightRef = useRef(false);
  const hasLoadedOnceRef = useRef(false);

  const load = useCallback(async (mode: 'initial' | 'refresh' | 'background') => {
    if (loadInFlightRef.current) return;
    loadInFlightRef.current = true;

    if (mode === 'initial') setLoadState('loading');
    try {
      const data = await securityAdminService.listUsuarios();
      setUsuarios(data);
      setLoadState('ready');
      hasLoadedOnceRef.current = true;
    } catch (err) {
      if (mode !== 'background') {
        setLoadErrorMsg(loadErrorMessage(err));
        setLoadState('error');
      }
    } finally {
      setRefreshing(false);
      loadInFlightRef.current = false;
    }
  }, []);

  // Carga al entrar y vuelve a consultar automáticamente al recuperar el foco (p. ej. al volver
  // del detalle de un usuario tras asignar/quitar un rol o activar/desactivar la cuenta).
  // Búsqueda, estado y filtro de rol no se tocan acá, así que sobreviven al refresh.
  useFocusEffect(
    useCallback(() => {
      load(hasLoadedOnceRef.current ? 'background' : 'initial');
    }, [load])
  );

  const onRefresh = () => {
    setRefreshing(true);
    load('refresh');
  };

  const rolesDisponibles = useMemo(
    () => Array.from(new Set(usuarios.flatMap((u) => u.rolesActivos))).sort(),
    [usuarios]
  );

  const usuariosFiltrados = useMemo(() => {
    const textoNormalizado = texto.trim().toLowerCase();
    return usuarios.filter((usuario) => {
      if (estado === 'activos' && !usuario.activo) return false;
      if (estado === 'inactivos' && usuario.activo) return false;
      if (rolFiltro && !usuario.rolesActivos.includes(rolFiltro)) return false;
      if (!textoNormalizado) return true;
      return usuario.email.toLowerCase().includes(textoNormalizado);
    });
  }, [usuarios, texto, estado, rolFiltro]);

  const openUsuario = (usuarioId: string) => {
    router.push({ pathname: '/admin/usuarios/[usuarioId]', params: { usuarioId } } as unknown as Href);
  };

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Usuarios" onRefresh={onRefresh} showBack />

      <View style={styles.filters}>
        <SearchField value={texto} onChangeText={setTexto} placeholder="Buscar por email" />
        <EstadoFilterRow value={estado} onChange={setEstado} />
        {rolesDisponibles.length > 0 ? (
          <FlatList
            horizontal
            showsHorizontalScrollIndicator={false}
            data={rolesDisponibles}
            keyExtractor={(item) => item}
            contentContainerStyle={styles.rolFilterContent}
            renderItem={({ item }) => {
              const active = rolFiltro === item;
              return (
                <Pressable
                  accessibilityRole="button"
                  accessibilityState={{ selected: active }}
                  accessibilityLabel={`Filtrar por rol ${item}`}
                  onPress={() => setRolFiltro((prev) => (prev === item ? undefined : item))}
                  style={[styles.rolChip, active && styles.rolChipActive]}
                >
                  <Text style={[styles.rolChipText, active && styles.rolChipTextActive]}>{item}</Text>
                </Pressable>
              );
            }}
          />
        ) : null}
      </View>

      {loadState === 'loading' ? (
        <AsyncStateView variant="loading" message="Cargando usuarios" />
      ) : loadState === 'error' ? (
        <AsyncStateView variant="error" message={loadErrorMsg ?? ''} onRetry={onRefresh} />
      ) : usuariosFiltrados.length === 0 ? (
        <AsyncStateView
          variant="empty"
          icon="groups"
          message={usuarios.length === 0 ? 'Todavía no hay usuarios.' : 'Ningún usuario coincide con el filtro.'}
        />
      ) : (
        <FlatList
          testID="usuarios-list"
          data={usuariosFiltrados}
          keyExtractor={(item) => item.usuarioId}
          renderItem={({ item }) => <UserRow usuario={item} onPress={() => openUsuario(item.usuarioId)} />}
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
  rolFilterContent: {
    gap: spacing.sm,
  },
  rolChip: {
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 6,
    paddingHorizontal: 12,
  },
  rolChipActive: {
    backgroundColor: colors.ink,
  },
  rolChipText: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.ink,
    textTransform: 'uppercase',
  },
  rolChipTextActive: {
    color: colors.paper,
  },
  list: {
    padding: spacing.lg,
  },
});
