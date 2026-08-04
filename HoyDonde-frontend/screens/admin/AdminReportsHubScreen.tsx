import React from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import type { Href } from 'expo-router';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';

interface EntradaReporte {
  key: string;
  titulo: string;
  descripcion: string;
  icono: React.ComponentProps<typeof MaterialIcons>['name'];
  href: string;
}

const ENTRADAS: EntradaReporte[] = [
  {
    key: 'events',
    titulo: 'Eventos (global)',
    descripcion: 'Actividad de eventos de cualquier organizador, con filtro opcional por organizador.',
    icono: 'event-note',
    href: '/admin/reports/events',
  },
  {
    key: 'sales',
    titulo: 'Ventas simuladas',
    descripcion: 'Cuándo y cuánto se vendió, en toda la plataforma o por organizador.',
    icono: 'insights',
    href: '/admin/reports/sales',
  },
  {
    key: 'security-audits',
    titulo: 'Auditoría de seguridad',
    descripcion: 'Mutaciones de roles, acciones y usuarios administradas desde /admin.',
    icono: 'fact-check',
    href: '/admin/reports/security-audits',
  },
];

/** Selector de reportes del Administrador (docs/api-mvp-plan.md §11.7): ambas entradas están gateadas por REPORTE_VER_GLOBAL en AdminHubScreen, así que llegar acá ya implica tener la acción. */
export default function AdminReportsHubScreen() {
  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Reportes" subtitle="Elegí qué reporte generar." showBack />
      <ScrollView contentContainerStyle={styles.scroll}>
        {ENTRADAS.map((entrada) => (
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
        ))}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.paper },
  scroll: { padding: spacing.lg },
  entryPressed: { opacity: 0.85 },
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
  entryTextBlock: { flex: 1 },
  entryTitle: { fontFamily: fonts.bold, fontSize: 17, color: colors.ink },
  entryDescription: { fontFamily: fonts.regular, fontSize: 13, color: colors.inkSoft, marginTop: 2 },
});
