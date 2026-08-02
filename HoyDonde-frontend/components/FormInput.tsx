import React from 'react';
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
  return (
    <View style={styles.formGroup}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        style={[styles.input, error && styles.inputError]}
        placeholder={placeholder}
        placeholderTextColor={`${colors.ink}80`}
        value={value}
        onChangeText={onChangeText}
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
    fontFamily: fonts.semiBold,
    fontSize: 14,
    marginBottom: spacing.xs,
    color: colors.ink,
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
