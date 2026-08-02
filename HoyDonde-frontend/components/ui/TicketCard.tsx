import React from 'react';
import { StyleSheet, View, ViewStyle } from 'react-native';

import { borderWidth, colors } from '@/constants/theme';
import { TicketDivider } from '@/components/ui/TicketDivider';

interface TicketCardProps {
  /** Bloque angosto: fecha de un evento o encabezado de una credencial. */
  stub: React.ReactNode;
  /** 'row' pone el talón a la izquierda (tarjetas de evento); 'column' lo pone arriba (pase/credencial). */
  orientation?: 'row' | 'column';
  children: React.ReactNode;
  style?: ViewStyle;
}

/** Entrada troquelada: talón + perforación + cuerpo principal. Elemento de identidad reutilizado en Cartelera y Perfil. */
export function TicketCard({ stub, orientation = 'row', children, style }: TicketCardProps) {
  const isRow = orientation === 'row';

  return (
    <View style={[styles.base, isRow ? styles.baseRow : styles.baseColumn, style]}>
      <View style={isRow ? styles.stubRow : styles.stubColumn}>{stub}</View>
      <TicketDivider orientation={isRow ? 'vertical' : 'horizontal'} />
      <View style={[styles.main, isRow ? styles.mainRow : styles.mainColumn]}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  base: {
    backgroundColor: colors.sand,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: 0,
  },
  baseRow: {
    flexDirection: 'row',
    alignItems: 'stretch',
  },
  baseColumn: {
    flexDirection: 'column',
  },
  stubRow: {
    width: 72,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  stubColumn: {
    paddingHorizontal: 16,
    paddingVertical: 12,
  },
  main: {
    flexShrink: 1,
  },
  mainRow: {
    flex: 1,
    paddingVertical: 12,
    paddingHorizontal: 14,
  },
  mainColumn: {
    paddingHorizontal: 16,
    paddingVertical: 14,
  },
});
