import React from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { Surface } from '@/components/ui/Surface';
import { ACCIONES } from '@/constants/acciones';
import { colors, fonts, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';

interface EntradaHub {
  key: string;
  titulo: string;
  descripcion: string;
  icono: React.ComponentProps<typeof MaterialIcons>['name'];
  href: string;
}

/**
 * Módulo administrativo (CLAUDE.md "Frontend 4"): cada entrada se muestra únicamente si el
 * usuario tiene alguna de las acciones efectivas que la habilitan — nunca por nombre de rol. El
 * backend vuelve a validar cada policy en cada request; esto solo evita mostrar botones muertos.
 */
export default function AdminHubScreen() {
  const { hasAccion } = useAuth();

  const puedeDarAltas = hasAccion(ACCIONES.USUARIO_CREAR_ADMIN) || hasAccion(ACCIONES.USUARIO_CREAR_ORGANIZADOR);
  const puedeVerRoles = hasAccion(ACCIONES.ROL_EDITAR);
  const puedeVerUsuarios = hasAccion(ACCIONES.USUARIO_VER_PERMISOS_EFECTIVOS);

  const entradas: EntradaHub[] = [];
  if (puedeDarAltas) {
    entradas.push({
      key: 'altas',
      titulo: 'Altas',
      descripcion: 'Crear cuentas de Administrador u Organizador.',
      icono: 'person-add',
      href: '/admin/altas',
    });
  }
  if (puedeVerRoles) {
    entradas.push({
      key: 'roles',
      titulo: 'Roles y acciones',
      descripcion: 'Administrar el catálogo de roles y qué acciones tiene cada uno.',
      icono: 'badge',
      href: '/admin/roles',
    });
  }
  if (puedeVerUsuarios) {
    entradas.push({
      key: 'usuarios',
      titulo: 'Usuarios',
      descripcion: 'Ver cuentas, roles asignados y permisos efectivos.',
      icono: 'groups',
      href: '/admin/usuarios',
    });
  }

  return (
    <View style={styles.container}>
      <EditorialHeader
        eyebrow="ADMINISTRACIÓN"
        title="Archivo administrativo"
        subtitle="Roles, acciones y usuarios del sistema."
        showBack
      />
      <ScrollView contentContainerStyle={styles.scroll}>
        {entradas.length === 0 ? (
          <Surface variant="sand" style={styles.emptyCard}>
            <MaterialIcons name="lock" size={28} color={colors.inkSoft} />
            <Text style={styles.emptyText}>Tu cuenta no tiene acciones de administración habilitadas.</Text>
          </Surface>
        ) : (
          entradas.map((entrada) => (
            <Pressable
              key={entrada.key}
              accessibilityRole="button"
              accessibilityLabel={entrada.titulo}
              onPress={() => router.push(entrada.href as Href)}
              style={({ pressed }) => pressed && styles.entryPressed}
            >
              <Surface style={styles.entry}>
                <View style={styles.entryIcon}>
                  <MaterialIcons name={entrada.icono} size={24} color={colors.ink} />
                </View>
                <View style={styles.entryTextBlock}>
                  <Text style={styles.entryTitle}>{entrada.titulo}</Text>
                  <Text style={styles.entryDescription}>{entrada.descripcion}</Text>
                </View>
                <MaterialIcons name="chevron-right" size={22} color={colors.inkSoft} />
              </Surface>
            </Pressable>
          ))
        )}
      </ScrollView>
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
  entryPressed: {
    opacity: 0.85,
  },
  entry: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    marginBottom: spacing.md,
    paddingVertical: spacing.md,
  },
  entryIcon: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: colors.sand,
    alignItems: 'center',
    justifyContent: 'center',
  },
  entryTextBlock: {
    flex: 1,
  },
  entryTitle: {
    fontFamily: fonts.bold,
    fontSize: 17,
    color: colors.ink,
  },
  entryDescription: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
    marginTop: 2,
  },
  emptyCard: {
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.xl,
  },
  emptyText: {
    fontFamily: fonts.medium,
    fontSize: 14,
    color: colors.inkSoft,
    textAlign: 'center',
  },
});
