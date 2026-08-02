import React from 'react';
import { KeyboardAvoidingView, Platform, SafeAreaView, ScrollView, StyleSheet, Text, View } from 'react-native';

import { colors, fonts, spacing } from '@/constants/theme';

interface AuthShellProps {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}

/** Shell editorial compartido por Login, Registro y Perfil. */
export function AuthShell({ title, subtitle, children }: AuthShellProps) {
  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <Text style={styles.wordmark}>HoyDonde?</Text>
          <View style={styles.header}>
            <Text style={styles.title}>{title}</Text>
            {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
          </View>
          {children}
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
    paddingTop: spacing.xxl,
  },
  wordmark: {
    fontFamily: fonts.black,
    fontSize: 20,
    color: colors.ink,
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
  subtitle: {
    fontFamily: fonts.regular,
    fontSize: 15,
    color: colors.ink,
    opacity: 0.7,
    marginTop: spacing.xs,
  },
});
