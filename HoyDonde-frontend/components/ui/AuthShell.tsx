import React from 'react';
import { KeyboardAvoidingView, Platform, SafeAreaView, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Surface } from '@/components/ui/Surface';
import { borderWidth, colors, fonts, spacing } from '@/constants/theme';

interface AuthShellProps {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}

/** Shell editorial compartido por Login y Registro: masthead + título con acento + panel tipo ventanilla. */
export function AuthShell({ title, subtitle, children }: AuthShellProps) {
  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <View style={styles.masthead}>
            <Text style={styles.wordmark}>HOYDONDE?</Text>
            <Text style={styles.tagline}>GUÍA DE EVENTOS</Text>
          </View>
          <View style={styles.hairline} />

          <View style={styles.header}>
            <Text style={styles.title}>{title}</Text>
            <View style={styles.accentBar} />
            {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
          </View>

          <Surface variant="sand" style={styles.panel}>
            {children}
          </Surface>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  flex: {
    flex: 1,
  },
  scroll: {
    flexGrow: 1,
    padding: spacing.lg,
    paddingTop: spacing.xl,
    paddingBottom: spacing.xxl,
  },
  masthead: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  wordmark: {
    fontFamily: fonts.bold,
    fontSize: 13,
    letterSpacing: 1.5,
    color: colors.ink,
  },
  tagline: {
    fontFamily: fonts.semiBold,
    fontSize: 11,
    letterSpacing: 1,
    color: colors.inkSoft,
  },
  hairline: {
    height: 1,
    backgroundColor: colors.ink,
    opacity: 0.25,
    marginTop: spacing.sm,
    marginBottom: spacing.xl,
  },
  header: {
    marginBottom: spacing.lg,
  },
  title: {
    fontFamily: fonts.black,
    fontSize: 30,
    color: colors.ink,
  },
  accentBar: {
    width: 44,
    height: 5,
    backgroundColor: colors.tomato,
    marginTop: spacing.xs,
  },
  subtitle: {
    fontFamily: fonts.regular,
    fontSize: 15,
    color: colors.inkSoft,
    marginTop: spacing.sm,
  },
  panel: {
    borderWidth: borderWidth.thick,
    padding: spacing.lg,
  },
});
