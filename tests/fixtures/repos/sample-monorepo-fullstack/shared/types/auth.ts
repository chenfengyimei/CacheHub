export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: 'admin' | 'user';
}

export interface TokenPair {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export interface AuthEvent {
  type: 'login' | 'logout' | 'refresh' | 'token_expired';
  userId: string;
  timestamp: number;
}
