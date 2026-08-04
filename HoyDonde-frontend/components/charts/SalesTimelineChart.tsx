import React, { useMemo } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';
import { formatPrecio } from '@/utils/format';
import type { VentasSerieBucket } from '@/services/reportService';

interface SalesTimelineChartProps {
  buckets: VentasSerieBucket[];
}

const CHART_HEIGHT = 120;
const BAR_WIDTH = 40;
const MIN_BAR_HEIGHT = 3;

/**
 * Evolución temporal del reporte de ventas (docs/api-mvp-plan.md §11, Parte 6): barras verticales
 * por día/semana/mes, con scroll horizontal si no entran todas. Sin librería de charts -Views y
 * texto plano-, identidad "ticket/ledger": tinta firme, acento tomato para el valor. Cada barra
 * lleva su etiqueta y valor escritos, nunca depende solo del color/altura para transmitir el dato.
 */
export function SalesTimelineChart({ buckets }: SalesTimelineChartProps) {
  const maxImporte = useMemo(() => Math.max(0, ...buckets.map((b) => b.importeEmitido)), [buckets]);

  if (buckets.length === 0) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Sin datos para el período elegido.</Text>
      </View>
    );
  }

  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
      {buckets.map((bucket) => {
        const alturaBarra = maxImporte <= 0 ? MIN_BAR_HEIGHT : Math.max(MIN_BAR_HEIGHT, (bucket.importeEmitido / maxImporte) * CHART_HEIGHT);
        return (
          <View
            key={bucket.periodoDesde}
            style={styles.column}
            accessible
            accessibilityLabel={`${bucket.etiqueta}: ${formatPrecio(bucket.importeEmitido)} emitidos en ${bucket.cantidadCompras} ${bucket.cantidadCompras === 1 ? 'compra' : 'compras'}`}
          >
            <Text style={styles.valueLabel} numberOfLines={1}>
              {bucket.importeEmitido > 0 ? formatPrecio(bucket.importeEmitido) : '—'}
            </Text>
            <View style={styles.barTrack}>
              <View style={[styles.bar, { height: alturaBarra, backgroundColor: bucket.importeEmitido > 0 ? colors.tomato : colors.sand }]} />
            </View>
            <Text style={styles.periodLabel} numberOfLines={1}>
              {bucket.etiqueta}
            </Text>
          </View>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scrollContent: { alignItems: 'flex-end', paddingBottom: spacing.xs, paddingHorizontal: spacing.xs },
  column: { width: BAR_WIDTH + spacing.sm, alignItems: 'center' },
  barTrack: { height: CHART_HEIGHT, justifyContent: 'flex-end' },
  bar: { width: BAR_WIDTH - spacing.sm, borderWidth: 1, borderColor: colors.ink, borderBottomWidth: 0 },
  valueLabel: { fontFamily: fonts.medium, fontSize: 9, color: colors.inkSoft, marginBottom: 2, width: BAR_WIDTH + spacing.sm, textAlign: 'center' },
  periodLabel: { fontFamily: fonts.bold, fontSize: 10, color: colors.ink, marginTop: spacing.xs, width: BAR_WIDTH + spacing.sm, textAlign: 'center' },
  empty: { paddingVertical: spacing.lg, alignItems: 'center' },
  emptyText: { fontFamily: fonts.regular, fontSize: 13, color: colors.inkSoft, fontStyle: 'italic' },
});
