import React from 'react';
import { StyleSheet, View } from 'react-native';

import { colors } from '@/constants/theme';

interface TicketDividerProps {
  /** 'vertical' separa un stub lateral (evento); 'horizontal' separa un stub superior (pase). */
  orientation?: 'vertical' | 'horizontal';
}

/** Línea de perforación con muescas troqueladas en los extremos, como el borde entre el talón y la entrada. */
export function TicketDivider({ orientation = 'vertical' }: TicketDividerProps) {
  const isVertical = orientation === 'vertical';
  const dots = new Array(isVertical ? 9 : 14).fill(null);

  return (
    <View style={[styles.track, isVertical ? styles.trackVertical : styles.trackHorizontal]}>
      <View style={[styles.notch, isVertical ? styles.notchTop : styles.notchStart]} />
      <View style={[styles.dotsLine, isVertical ? styles.dotsColumn : styles.dotsRow]}>
        {dots.map((_, index) => (
          <View key={index} style={styles.dot} />
        ))}
      </View>
      <View style={[styles.notch, isVertical ? styles.notchBottom : styles.notchEnd]} />
    </View>
  );
}

const NOTCH_SIZE = 14;

const styles = StyleSheet.create({
  track: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  trackVertical: {
    width: NOTCH_SIZE,
  },
  trackHorizontal: {
    height: NOTCH_SIZE,
    flexDirection: 'row',
  },
  dotsLine: {
    alignItems: 'center',
    justifyContent: 'space-between',
    flexGrow: 1,
    flexShrink: 1,
  },
  dotsColumn: {
    width: 2,
  },
  dotsRow: {
    height: 2,
    flexDirection: 'row',
  },
  dot: {
    width: 3,
    height: 3,
    borderRadius: 2,
    backgroundColor: colors.ink,
    opacity: 0.35,
  },
  notch: {
    width: NOTCH_SIZE,
    height: NOTCH_SIZE,
    borderRadius: NOTCH_SIZE,
    borderWidth: 1,
    borderColor: colors.ink,
    backgroundColor: colors.paper,
  },
  notchTop: {
    marginBottom: 4,
  },
  notchBottom: {
    marginTop: 4,
  },
  notchStart: {
    marginRight: 4,
  },
  notchEnd: {
    marginLeft: 4,
  },
});
