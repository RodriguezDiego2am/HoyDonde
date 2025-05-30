import React, { useState } from 'react';
import { 
  View, 
  Text, 
  StyleSheet, 
  TouchableOpacity, 
  ActivityIndicator, 
  Alert,
  SafeAreaView,
  Image,
  KeyboardAvoidingView,
  Platform,
  ScrollView
} from 'react-native';
import { router } from 'expo-router';
import FormInput from '../components/FormInput';
import { authService } from '../services/APIService';
import { StatusBar } from 'expo-status-bar';
import { Colors } from '../constants/Colors';

export default function LoginScreen() {
  const [formData, setFormData] = useState({
    identifier: '',
    password: ''
  });
  const [errors, setErrors] = useState<{
    identifier?: string;
    password?: string;
  }>({});
  const [loading, setLoading] = useState(false);

  const validate = () => {
    const newErrors: {
      identifier?: string;
      password?: string;
    } = {};
    
    if (!formData.identifier) {
      newErrors.identifier = 'El email es obligatorio';
    }
    
    if (!formData.password) {
      newErrors.password = 'La contraseña es obligatoria';
    }
    
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleLogin = async () => {
    if (validate()) {
      setLoading(true);
      try {
        await authService.login(formData.identifier, formData.password);
        // Navegar a la pantalla principal
        router.replace('/(tabs)');
      } catch (error: any) {
        let errorMessage = 'Error al iniciar sesión';
        if (error.response && error.response.data) {
          errorMessage = error.response.data.message || errorMessage;
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
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      >
        <ScrollView contentContainerStyle={styles.scrollContainer}>
          <View style={styles.header}>
            {/* Puedes reemplazar esto con tu logo */}
            <Text style={styles.logo}>HoyDonde</Text>
          </View>
          
          <View style={styles.formContainer}>
            <Text style={styles.title}>Iniciar Sesión</Text>
            
            <FormInput
              label="Email o Usuario"
              value={formData.identifier}
              onChangeText={(text) => handleChange('identifier', text)}
              placeholder="ejemplo@correo.com"
              error={errors.identifier}
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
            
            <TouchableOpacity style={styles.forgotPassword}>
              <Text style={styles.forgotPasswordText}>¿Olvidaste tu contraseña?</Text>
            </TouchableOpacity>
            
            <TouchableOpacity 
              style={styles.button} 
              onPress={handleLogin}
              disabled={loading}
            >
              {loading ? (
                <ActivityIndicator color="#fff" />
              ) : (
                <Text style={styles.buttonText}>Iniciar Sesión</Text>
              )}
            </TouchableOpacity>
            
            <View style={styles.registerContainer}>
              <Text style={styles.registerText}>¿No tienes una cuenta? </Text>
              <TouchableOpacity onPress={() => router.push('/register')}>
                <Text style={styles.registerLink}>Regístrate</Text>
              </TouchableOpacity>
            </View>
          </View>
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
  },
  header: {
    alignItems: 'center',
    paddingTop: 50,
    paddingBottom: 20,
  },
  logo: {
    fontSize: 28,
    fontWeight: 'bold',
    color: '#007bff',
  },
  formContainer: {
    padding: 20,
  },
  title: {
    fontSize: 24,
    fontWeight: 'bold',
    color: '#333',
    marginBottom: 20,
    textAlign: 'center',
  },
  forgotPassword: {
    alignSelf: 'flex-end',
    marginBottom: 20,
  },
  forgotPasswordText: {
    color: '#007bff',
  },
  button: {
    backgroundColor: '#007bff',
    height: 50,
    borderRadius: 8,
    justifyContent: 'center',
    alignItems: 'center',
  },
  buttonText: {
    color: '#fff',
    fontSize: 16,
    fontWeight: 'bold',
  },
  registerContainer: {
    flexDirection: 'row',
    justifyContent: 'center',
    marginTop: 20,
  },
  registerText: {
    color: '#333',
  },
  registerLink: {
    color: '#007bff',
    fontWeight: 'bold',
  },
});