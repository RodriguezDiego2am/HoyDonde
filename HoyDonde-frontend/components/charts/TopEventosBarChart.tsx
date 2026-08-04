import React, { useMemo } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';
import { formatPrecio } from '@/utils/format';

export interface TopEventoBarItem {
  key: string;
  nombre: string;
  importeEmitido: number;
  entradasEmitidas?: number;
}

interface TopEventosBarChartProps {
  items: TopEventoBarItem[];
  /** Si se provee, cada fila se vuelve presionable (p. ej. para acotar el reporte a ese evento). */
  onPressItem?: (item: TopEventoBarItem) => void;
}

/**
 * "Top eventos por importe emitido" (docs/api-mvp-plan.md §11, Parte 6): barras horizontales,
 * nombre + barra proporcional + importe escrito + cantidad de entradas opcional. El valor máximo
 * del conjunto controla la escala; empates quedan con la misma longitud de barra (el orden ya
 * viene resuelto por el backend); si todos los importes son 0, todas las barras quedan al mínimo
 * visible sin dividir por cero.
 */
export function TopEventosBarChart({ items, onPressItem }: TopEventosBarChartProps) {
  const maxImporte = useMemo(() => Math.max(0, ...items.map((i) => i.importeEmitido)), [items]);

  if (items.length === 0) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Sin eventos con ventas en el período elegido.</Text>
      </View>
    );
  }

  return (
    <View>
      {items.map((item) => {
        const anchoPorcentaje = maxImporte <= 0 ? 4 : Math.max(4, (item.importeEmitido / maxImporte) * 100);
        const entradasTexto = item.entradasEmitidas !== undefined ? ` · ${item.entradasEmitidas} ${item.entradasEmitidas === 1 ? 'entrada' : 'entradas'}` : '';
        const contenido = (
          <>
            <Text style={styles.nombre} numberOfLines={1}>
              {item.nombre}
            </Text>
            <View style={styles.trackRow}>
              <View style={styles.track}>
                <View style={[styles.bar, { width: `${anchoPorcentaje}%` }]} />
              </View>
              <Text style={styles.valor} numberOfLines={1}>
                {formatPrecio(item.importeEmitido)}
              </Text>
            </View>
            {item.entradasEmitidas !== undefined ? (
              <Text style={styles.entradas}>{item.entradasEmitidas} {item.entradasEmitidas === 1 ? 'entrada' : 'entradas'}</Text>
            ) : null}
          </>
        );

        if (onPressItem) {
          return (
            <Pressable
              key={item.key}
              style={styles.row}
              accessibilityRole="button"
              accessibilityLabel={`${item.nombre}: ${formatPrecio(item.importeEmitido)}${entradasTexto}. Ver solo este evento`}
              onPress={() => onPressItem(item)}
            >
              {contenido}
            </Pressable>
          );
        }

        return (
          <View key={item.key} style={styles.row} accessible accessibilityLabel={`${item.nombre}: ${formatPrecio(item.importeEmitido)}${entradasTexto}`}>
            {contenido}
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { marginBottom: spacing.md },
  nombre: { fontFamily: fonts.bold, fontSize: 13, color: colors.ink, marginBottom: spacing.xs },
  trackRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  track: { flex: 1, height: 14, borderWidth: 1, borderColor: colors.ink, backgroundColor: colors.paper },
  bar: { height: '100%', backgroundColor: colors.tomato },
  valor: { fontFamily: fonts.bold, fontSize: 12, color: colors.ink, minWidth: 70, textAlign: 'right' },
  entradas: { fontFamily: fonts.regular, fontSize: 11, color: colors.inkSoft, marginTop: 2 },
  empty: { paddingVertical: spacing.lg, alignItems: 'center' },
  emptyText: { fontFamily: fonts.regular, fontSize: 13, color: colors.inkSoft, fontStyle: 'italic' },
});
