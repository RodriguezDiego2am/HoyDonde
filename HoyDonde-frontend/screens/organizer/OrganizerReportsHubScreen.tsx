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
    titulo: 'Desempeño de eventos',
    descripcion: 'Ocupación, asistencia e importe emitido de tus propios eventos.',
    icono: 'event-note',
    href: '/organizer/reports/events',
  },
  {
    key: 'sales',
    titulo: 'Ventas simuladas',
    descripcion: 'Cuándo y cuánto se vendió: compras, entradas e importe emitido en el tiempo.',
    icono: 'insights',
    href: '/organizer/reports/sales',
  },
];

// Selector de reportes del Organizador (docs/api-mvp-plan.md §11): ambas entradas están gateadas
// por REPORTE_VER_PROPIO en OrganizerEventsListScreen (el único botón que llega hasta acá), así
// que alcanzar este hub ya implica tener la acción — mismo criterio que AdminReportsHubScreen.
export default function OrganizerReportsHubScreen() {
  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ORGANIZACIÓN" title="Reportes" subtitle="Elegí qué reporte generar." showBack />
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
