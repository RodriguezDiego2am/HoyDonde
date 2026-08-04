import React from 'react';
import { StyleSheet, Text, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';

interface OcupacionAsistenciaBarsProps {
  porcentajeOcupacion: number;
  porcentajeAsistencia: number;
}

function ProgressBar({ label, porcentaje, color }: { label: string; porcentaje: number; color: string }) {
  const ancho = Math.max(0, Math.min(100, porcentaje));
  return (
    <View style={styles.barBlock} accessible accessibilityLabel={`${label}: ${porcentaje.toFixed(1)}%`}>
      <View style={styles.barHeader}>
        <Text style={styles.barLabel}>{label}</Text>
        <Text style={styles.barValue}>{porcentaje.toFixed(1)}%</Text>
      </View>
      <View style={styles.track}>
        <View style={[styles.fill, { width: `${ancho}%`, backgroundColor: color }]} />
      </View>
    </View>
  );
}

/**
 * Comparación ocupación vs. asistencia (docs/api-mvp-plan.md §11, Parte 6): dos barras de progreso
 * etiquetadas, misma escala 0-100%. Ocupación = entradas emitidas / capacidad; Asistencia = usadas
 * / emitidas -significados sin cambiar respecto del backend (ReporteEventoDetalleDto), esto es
 * solo la lectura visual-. Colores distintos por barra, pero el porcentaje siempre está escrito.
 */
export function OcupacionAsistenciaBars({ porcentajeOcupacion, porcentajeAsistencia }: OcupacionAsistenciaBarsProps) {
  return (
    <View>
      <ProgressBar label="Ocupación" porcentaje={porcentajeOcupacion} color={colors.cobalt} />
      <ProgressBar label="Asistencia" porcentaje={porcentajeAsistencia} color={colors.tomato} />
    </View>
  );
}

const styles = StyleSheet.create({
  barBlock: { marginBottom: spacing.sm },
  barHeader: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 4 },
  barLabel: { fontFamily: fonts.bold, fontSize: 12, color: colors.ink, textTransform: 'uppercase', letterSpacing: 0.5 },
  barValue: { fontFamily: fonts.bold, fontSize: 12, color: colors.ink },
  track: { height: 10, borderWidth: 1, borderColor: colors.ink, backgroundColor: colors.paper },
  fill: { height: '100%' },
});
