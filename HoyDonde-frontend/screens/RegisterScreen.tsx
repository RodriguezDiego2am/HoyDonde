import React, { useState } from 'react';
import { 
  View, 
  Text, 
  TouchableOpacity, 
  StyleSheet, 
  ScrollView,
  ActivityIndicator,
  Alert,
  SafeAreaView,
  KeyboardAvoidingView,
  Platform
} from 'react-native';
import { router } from 'expo-router';
import FormInput from '../components/FormInput';
import { authService } from '../services/APIService';
import { StatusBar } from 'expo-status-bar';

export default function RegisterScreen() {
  const [formData, setFormData] = useState({
    email: '',
    password: '',
    confirmPassword: '',
    fullName: '',
    dni: '',
    phoneNumber: ''
  });
  const [errors, setErrors] = useState<{
    email?: string;
    password?: string;
    confirmPassword?: string;
    fullName?: string;
    dni?: string;
    phoneNumber?: string;
  }>({});
  const [loading, setLoading] = useState(false);

  const validate = () => {
    const newErrors: {
      email?: string;
      password?: string;
      confirmPassword?: string;
      fullName?: string;
      dni?: string;
      phoneNumber?: string;
    } = {};
    
    // Validación email
    if (!formData.email) {
      newErrors.email = 'El email es obligatorio';
    } else if (!/\S+@\S+\.\S+/.test(formData.email)) {
      newErrors.email = 'Email inválido';
    }
    
    // Validación contraseña
    if (!formData.password) {
      newErrors.password = 'La contraseña es obligatoria';
    } else if (formData.password.length < 6) {
      newErrors.password = 'La contraseña debe tener al menos 6 caracteres';
    }
    
    // Validación confirmación de contraseña
    if (formData.password !== formData.confirmPassword) {
      newErrors.confirmPassword = 'Las contraseñas no coinciden';
    }
    
    // Validación nombre completo
    if (!formData.fullName) {
      newErrors.fullName = 'El nombre completo es obligatorio';
    }
    
    // Validación DNI
    if (!formData.dni) {
      newErrors.dni = 'El DNI es obligatorio';
    }
    
    // Validación número de teléfono
    if (!formData.phoneNumber) {
      newErrors.phoneNumber = 'El número de teléfono es obligatorio';
    }
    
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleRegister = async () => {
    if (validate()) {
      setLoading(true);
      try {
        const userData = {
          email: formData.email,
          password: formData.password,
          fullName: formData.fullName,
          dni: formData.dni,
          phoneNumber: formData.phoneNumber
        };
        
        await authService.registerCliente(userData);
        
        Alert.alert(
          'Registro exitoso',
          'Tu cuenta ha sido creada correctamente',
          [{ text: 'OK', onPress: () => router.push('/login') }]
        );
      } catch (error: any) {
        let errorMessage = 'Error al registrarse';
        if (error.response && error.response.data) {
          if (Array.isArray(error.response.data)) {
            errorMessage = error.response.data.join('\n');
          } else if (typeof error.response.data === 'string') {
            errorMessage = error.response.data;
          } else if (error.response.data.errors) {
            errorMessage = Object.values(error.response.data.errors).join('\n');
          }
        }
        Alert.alert('Error', errorMessage);
      } finally {
        setLoading(false);
      }
    }
  };

  const handleChange = (name: string, value: string) => {
    setFormData(prevState => ({
      ...prevState,
      [name]: value
    }));
  };

  return (
    <SafeAreaView style={styles.container}>
      <StatusBar style="dark" />
      <KeyboardAvoidingView
        style={{ flex: 1 }}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <ScrollView contentContainerStyle={styles.scrollContainer}>
          <Text style={styles.title}>Crear una cuenta</Text>

          <FormInput
            label="Email"
            value={formData.email}
            onChangeText={(text) => handleChange('email', text)}
            placeholder="ejemplo@correo.com"
            error={errors.email}
            keyboardType="email-address"
          />

          <FormInput
            label="Contraseña"
            value={formData.password}
            onChangeText={(text) => handleChange('password', text)}
            placeholder="Tu contraseña"
            secureTextEntry={true}
            error={errors.password}
          />

          <FormInput
            label="Confirmar Contraseña"
            value={formData.confirmPassword}
            onChangeText={(text) => handleChange('confirmPassword', text)}
            placeholder="Confirma tu contraseña"
            secureTextEntry={true}
            error={errors.confirmPassword}
          />

          <FormInput
            label="Nombre Completo"
            value={formData.fullName}
            onChangeText={(text) => handleChange('fullName', text)}
            placeholder="Tu nombre completo"
            error={errors.fullName}
            autoCapitalize="words"
          />

          <FormInput
            label="DNI"
            value={formData.dni}
            onChangeText={(text) => handleChange('dni', text)}
            placeholder="Tu DNI"
            error={errors.dni}
            keyboardType="numeric"
          />

          <FormInput
            label="Número de Teléfono"
            value={formData.phoneNumber}
            onChangeText={(text) => handleChange('phoneNumber', text)}
            placeholder="Tu número de teléfono"
            error={errors.phoneNumber}
            keyboardType="phone-pad"
          />

          <TouchableOpacity 
            style={styles.button} 
            onPress={handleRegister}
            disabled={loading}
          >
            {loading ? (
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={styles.buttonText}>Registrarse</Text>
            )}
          </TouchableOpacity>

          <TouchableOpacity 
            style={styles.loginLink}
            onPress={() => router.push('/login')}
          >
            <Text style={styles.loginLinkText}>
              ¿Ya tienes una cuenta? Iniciar sesión
            </Text>
          </TouchableOpacity>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f8f9fa',
  },
  scrollContainer: {
    flexGrow: 1,
    padding: 20,
  },
  title: {
    fontSize: 24,
    fontWeight: 'bold',
    color: '#333',
    marginBottom: 20,
    textAlign: 'center',
  },
  button: {
    backgroundColor: '#007bff',
    height: 50,
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
    marginTop: 10,
  },
  buttonText: {
    color: '#fff',
    fontSize: 16,
    fontWeight: 'bold',
  },
  loginLink: {
    marginTop: 20,
    alignItems: 'center',
    marginBottom: 30,
  },
  loginLinkText: {
    color: '#007bff',
    fontSize: 16,
  },
});