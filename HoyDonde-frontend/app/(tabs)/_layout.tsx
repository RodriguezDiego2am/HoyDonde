import { Tabs } from 'expo-router';
import React from 'react';

import { IconSymbol } from '@/components/ui/IconSymbol';
import { borderWidth, colors, fonts } from '@/constants/theme';

export default function TabLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.tomato,
        tabBarInactiveTintColor: colors.ink,
        tabBarLabelStyle: { fontFamily: fonts.semiBold, fontSize: 12 },
        tabBarStyle: {
          backgroundColor: colors.paper,
          borderTopColor: colors.ink,
          borderTopWidth: borderWidth.thin,
        },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Cartelera',
          tabBarIcon: ({ color }) => <IconSymbol size={26} name="house.fill" color={color} />,
        }}
      />
      <Tabs.Screen
        name="explore"
        options={{
          title: 'Perfil',
          tabBarIcon: ({ color }) => <IconSymbol size={26} name="person.fill" color={color} />,
        }}
      />
    </Tabs>
  );
}
