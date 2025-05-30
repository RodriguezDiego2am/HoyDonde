import axios from 'axios';
import * as SecureStore from 'expo-secure-store';

const API_URL = 'https://tu-api-url.com/api'; // Reemplaza con la URL real de tu API

// Claves para almacenamiento seguro
const TOKEN_KEY = 'hoydonde_auth_token';
const USER_KEY = 'hoydonde_user_data';

// Funciones para gestionar el token y datos de usuario
export const storeToken = async (token: string): Promise<boolean> => {
  try {
    await SecureStore.setItemAsync(TOKEN_KEY, token);
    return true;
  } catch (error) {
    console.error('Error al guardar el token', error);
    return false;
  }
};

export const getToken = async (): Promise<string | null> => {
  try {
    return await SecureStore.getItemAsync(TOKEN_KEY);
  } catch (error) {
    console.error('Error al obtener el token', error);
    return null;
  }
};

export const storeUser = async (userData: any): Promise<boolean> => {
  try {
    await SecureStore.setItemAsync(USER_KEY, JSON.stringify(userData));
    return true;
  } catch (error) {
    console.error('Error al guardar los datos del usuario', error);
    return false;
  }
};

export const getUser = async (): Promise<any> => {
  try {
    const data = await SecureStore.getItemAsync(USER_KEY);
    return data ? JSON.parse(data) : null;
  } catch (error) {
    console.error('Error al obtener los datos del usuario', error);
    return null;
  }
};

export const clearStorage = async (): Promise<boolean> => {
  try {
    await SecureStore.deleteItemAsync(TOKEN_KEY);
    await SecureStore.deleteItemAsync(USER_KEY);
    return true;
  } catch (error) {
    console.error('Error al limpiar el almacenamiento', error);
    return false;
  }
};

// Configuración global para axios
const apiClient = axios.create({
  baseURL: API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Interceptor para manejar tokens
apiClient.interceptors.request.use(
  async (config) => {
    const token = await getToken();
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// Funciones específicas para autenticación
interface LoginResponse {
  token: string;
  userName: string;
  roles: string[];
}

interface RegisterClienteData {
  email: string;
  password: string;
  fullName: string;
  dni: string;
  phoneNumber: string;
}

export const authService = {
  login: async (identifier: string, password: string): Promise<LoginResponse> => {
    const response = await apiClient.post<LoginResponse>('/auth/login', { identifier, password });
    
    // Guardar token y datos del usuario
    if (response.data && response.data.token) {
      await storeToken(response.data.token);
      await storeUser({
        userName: response.data.userName,
        roles: response.data.roles
      });
    }
    
    return response.data;
  },
  
  registerCliente: async (userData: RegisterClienteData) => {
    return await apiClient.post('/users/register-cliente', userData);
  },
  
  logout: async (): Promise<void> => {
    await clearStorage();
  }
};

export default apiClient;