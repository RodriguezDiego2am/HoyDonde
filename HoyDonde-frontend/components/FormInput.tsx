import React, { useState } from 'react';
import { StyleSheet, Text, TextInput, View } from 'react-native';

import { borderWidth, colors, fonts, radii, spacing } from '../constants/theme';

interface FormInputProps {
  label: string;
  value: string;
  onChangeText: (text: string) => void;
  placeholder?: string;
  secureTextEntry?: boolean;
  error?: string | null;
  keyboardType?: 'default' | 'email-address' | 'numeric' | 'phone-pad';
  autoCapitalize?: 'none' | 'sentences' | 'words' | 'characters';
}

const FormInput: React.FC<FormInputProps> = ({
  label,
  value,
  onChangeText,
  placeholder,
  secureTextEntry = false,
  error,
  keyboardType = 'default',
  autoCapitalize = 'none',
}) => {
  const [isFocused, setIsFocused] = useState(false);

  return (
    <View style={styles.formGroup}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        style={[styles.input, isFocused && styles.inputFocused, error && styles.inputError]}
        placeholder={placeholder}
        placeholderTextColor={colors.inkSoft}
        value={value}
        onChangeText={onChangeText}
        onFocus={() => setIsFocused(true)}
        onBlur={() => setIsFocused(false)}
        secureTextEntry={secureTextEntry}
        keyboardType={keyboardType}
        autoCapitalize={autoCapitalize}
        accessibilityLabel={label}
      />
      {error ? <Text style={styles.errorText}>{error}</Text> : null}
    </View>
  );
};

const styles = StyleSheet.create({
  formGroup: {
    marginBottom: spacing.md,
  },
  label: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    marginBottom: spacing.xs,
    color: colors.inkSoft,
  },
  input: {
    height: 50,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingHorizontal: spacing.md,
    fontFamily: fonts.regular,
    fontSize: 16,
    color: colors.ink,
    backgroundColor: colors.paper,
  },
  inputFocused: {
    borderColor: colors.cobalt,
    borderWidth: borderWidth.thick,
  },
  inputError: {
    borderColor: colors.error,
    borderWidth: borderWidth.thick,
  },
  errorText: {
    fontFamily: fonts.medium,
    color: colors.error,
    fontSize: 13,
    marginTop: spacing.xs,
  },
});

export default FormInput;
