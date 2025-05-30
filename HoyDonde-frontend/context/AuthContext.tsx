import React, { createContext, useState, useContext, useEffect } from 'react';
import { authService, getToken, getUser, clearStorage } from '../services/APIService';

interface AuthContextType {
  isAuthenticated: boolean;
  user: any | null;
  login: (identifier: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  loading: boolean;
}

const AuthContext = createContext<AuthContextType>({
  isAuthenticated: false,
  user: null,
  login: async () => {},
  logout: async () => {},
  loading: true
});

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [user, setUser] = useState<any | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const checkAuth = async () => {
      const token = await getToken();
      const userData = await getUser();
      if (token && userData) {
        setIsAuthenticated(true);
        setUser(userData);
      }
      setLoading(false);
    };
    checkAuth();
  }, []);

  const login = async (identifier: string, password: string) => {
    setLoading(true);
    await authService.login(identifier, password);
    const userData = await getUser();
    setIsAuthenticated(true);
    setUser(userData);
    setLoading(false);
  };

  const logout = async () => {
    setLoading(true);
    await clearStorage();
    setIsAuthenticated(false);
    setUser(null);
    setLoading(false);
  };

  return (
    <AuthContext.Provider value={{ isAuthenticated, user, login, logout, loading }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => useContext(AuthContext);