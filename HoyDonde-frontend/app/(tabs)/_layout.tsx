import { Tabs } from 'expo-router';
import React from 'react';
import { StyleSheet, View } from 'react-native';

import { IconSymbol } from '@/components/ui/IconSymbol';
import { borderWidth, colors, fonts } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';

type TabIconName = 'ticket.fill' | 'wallet.pass.fill' | 'qrcode.viewfinder';

/** Insignia circular tipo sello: se rellena de tomate cuando la pestaña está activa, como una entrada validada. */
function TabIcon({ focused, name }: { focused: boolean; name: TabIconName }) {
  return (
    <View style={[styles.iconBadge, focused && styles.iconBadgeActive]}>
      <IconSymbol size={20} name={name} color={focused ? colors.paper : colors.inkSoft} />
    </View>
  );
}

export default function TabLayout() {
  const { hasAccion } = useAuth();
  // La pestaña se resuelve por acción efectiva del usuario (nunca por nombre de rol
  // hardcodeado): href: null la oculta de la barra sin desmontar la ruta.
  const puedeVerEntradas = hasAccion('TICKET_VER_PROPIO');

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.ink,
        tabBarInactiveTintColor: colors.inkSoft,
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
          tabBarIcon: ({ focused }) => <TabIcon focused={focused} name="wallet.pass.fill" />,
        }}
      />
    </Tabs>
  );
}

const styles = StyleSheet.create({
  bar: {
    backgroundColor: colors.paper,
    borderTopColor: colors.ink,
    borderTopWidth: borderWidth.thick,
    paddingTop: 6,
  },
  item: {
    paddingTop: 2,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 0.6,
    textTransform: 'uppercase',
  },
  iconBadge: {
    width: 32,
    height: 32,
    borderRadius: 16,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 2,
  },
  iconBadgeActive: {
    backgroundColor: colors.tomato,
  },
});
