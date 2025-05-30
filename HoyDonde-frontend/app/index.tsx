import { useEffect } from 'react';
import { router } from 'expo-router';
import { useAuth } from '../context/AuthContext';
import { View, ActivityIndicator } from 'react-native';

export default function Index() {
  const { isAuthenticated, loading } = useAuth();

  useEffect(() => {
    if (!loading) {
      router.replace(isAuthenticated ? '/(tabs)' : '/login');
    }
  }, [isAuthenticated, loading]);

  return (
    <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center' }}>
      <ActivityIndicator size="large" color="#007bff" />
    </View>
  );
}