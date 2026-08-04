import React, { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { colors, spacing } from '../constants/theme';
import FormInput from './FormInput';

interface PasswordFormInputProps {
  label: string;
  value: string;
  onChangeText: (text: string) => void;
  placeholder?: string;
  error?: string | null;
}

/** FormInput especializado en contraseñas: agrega un toggle mostrar/ocultar sin tocar FormInput. */
export default function PasswordFormInput({ label, value, onChangeText, placeholder, error }: PasswordFormInputProps) {
  const [visible, setVisible] = useState(false);

  return (
    <View style={styles.wrapper}>
      <FormInput
        label={label}
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        secureTextEntry={!visible}
        error={error}
      />
      <Pressable
        onPress={() => setVisible((current) => !current)}
        style={styles.toggle}
        hitSlop={8}
        accessibilityRole="button"
        accessibilityLabel={visible ? `Ocultar ${label.toLowerCase()}` : `Mostrar ${label.toLowerCase()}`}
      >
        <MaterialIcons name={visible ? 'visibility-off' : 'visibility'} size={20} color={colors.inkSoft} />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    position: 'relative',
  },
  toggle: {
    position: 'absolute',
    right: spacing.md,
    top: 33,
  },
});
