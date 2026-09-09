import { Tabs } from 'expo-router';
import React from 'react';
import { StyleSheet, View } from 'react-native';

import { IconSymbol } from '@/components/ui/IconSymbol';
import { ACCIONES } from '@/constants/acciones';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';

type TabIconName = 'ticket.fill' | 'person.fill' | 'qrcode.viewfinder';

/** Insignia de pestaña: el estado activo se sella en lima, no solo por color de texto. */
function TabIcon({ focused, name }: { focused: boolean; name: TabIconName }) {
  return (
    <View style={[styles.iconBadge, focused && styles.iconBadgeActive]}>
      <IconSymbol size={20} name={name} color={focused ? colors.ink : colors.sand} />
    </View>
  );
}

export default function TabLayout() {
  const { hasAccion } = useAuth();
  // La pestaña se resuelve por acción efectiva del usuario (nunca por nombre de rol
  // hardcodeado): href: null la oculta de la barra sin desmontar la ruta.
  const puedeVerEntradas = hasAccion(ACCIONES.TICKET_VER_PROPIO);

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.lime,
        tabBarInactiveTintColor: colors.sand,
        tabBarLabelStyle: styles.label,
        tabBarItemStyle: styles.item,
        tabBarStyle: styles.bar,
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Cartelera',
          tabBarIcon: ({ focused }) => <TabIcon focused={focused} name="ticket.fill" />,
        }}
      />
      <Tabs.Screen
        name="tickets"
        options={{
          title: 'Mis entradas',
          href: puedeVerEntradas ? undefined : null,
          tabBarIcon: ({ focused }) => <TabIcon focused={focused} name="qrcode.viewfinder" />,
        }}
      />
      <Tabs.Screen
        name="explore"
        options={{
          title: 'Perfil',
          tabBarIcon: ({ focused }) => <TabIcon focused={focused} name="person.fill" />,
        }}
      />
    </Tabs>
  );
}

const styles = StyleSheet.create({
  bar: {
    backgroundColor: colors.ink,
    borderTopColor: colors.tomato,
    borderTopWidth: borderWidth.thick,
    minHeight: 76,
    paddingTop: spacing.sm,
    paddingBottom: spacing.lg,
    borderTopLeftRadius: radii.lg,
    borderTopRightRadius: radii.lg,
  },
  item: {
    minHeight: 52,
    paddingTop: spacing.xs,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 10,
    letterSpacing: 0.6,
    textTransform: 'uppercase',
  },
  iconBadge: {
    width: 38,
    height: 38,
    borderRadius: radii.md,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.xs,
  },
  iconBadgeActive: {
    backgroundColor: colors.lime,
  },
});
