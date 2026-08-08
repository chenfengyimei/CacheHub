import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { AuthUser, TokenPair } from '../../../shared/types/auth';
import { apiClient } from '../api/client';

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshTokens: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Check for existing tokens on mount
    const token = localStorage.getItem('accessToken');
    if (token) {
      apiClient.get<AuthUser>('/auth/me')
        .then(u => setUser(u))
        .catch(() => localStorage.removeItem('accessToken'))
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, []);

  const login = async (email: string, password: string) => {
    const { user, tokens } = await apiClient.post<{ user: AuthUser; tokens: TokenPair }>(
      '/auth/login',
      { email, password }
    );
    localStorage.setItem('accessToken', tokens.accessToken);
    localStorage.setItem('refreshToken', tokens.refreshToken);
    setUser(user);
  };

  const logout = async () => {
    try {
      await apiClient.post('/auth/logout', {});
    } finally {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      setUser(null);
    }
  };

  const refreshTokens = async () => {
    const refreshToken = localStorage.getItem('refreshToken');
    if (!refreshToken) return;
    const { tokens } = await apiClient.post<{ tokens: TokenPair }>('/auth/refresh', { refreshToken });
    localStorage.setItem('accessToken', tokens.accessToken);
    localStorage.setItem('refreshToken', tokens.refreshToken);
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, logout, refreshTokens }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
