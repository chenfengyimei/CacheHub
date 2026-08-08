import { TokenManager } from './TokenManager';
import { HttpClient } from '../config/http';
import { AuthUser, LoginRequest, TokenPair } from './types';

export class AuthService {
  private tokenManager: TokenManager;
  private http: HttpClient;
  private refreshPromise: Promise<TokenPair> | null = null;

  constructor(http: HttpClient, tokenManager: TokenManager) {
    this.http = http;
    this.tokenManager = tokenManager;
  }

  async login(req: LoginRequest): Promise<AuthUser> {
    const res = await this.http.post<{ user: AuthUser; tokens: TokenPair }>('/auth/login', req);
    this.tokenManager.setTokens(res.tokens);
    return res.user;
  }

  async logout(): Promise<void> {
    const token = this.tokenManager.getAccessToken();
    if (token) {
      await this.http.post('/auth/logout', {}, { Authorization: `Bearer ${token}` });
    }
    this.tokenManager.clearTokens();
  }

  async refreshToken(): Promise<TokenPair> {
    const refreshToken = this.tokenManager.getRefreshToken();
    if (!refreshToken) {
      throw new Error('No refresh token available');
    }

    // BUG: Does not handle 401 retries — if the refresh token is expired,
    // this throws an unhandled error instead of clearing tokens and redirecting.
    const res = await this.http.post<{ tokens: TokenPair }>('/auth/refresh', { refreshToken });
    this.tokenManager.setTokens(res.tokens);
    return res.tokens;
  }

  async getProfile(): Promise<AuthUser> {
    const token = this.tokenManager.getAccessToken();
    if (!token) throw new Error('Not authenticated');
    const res = await this.http.get<{ user: AuthUser }>('/auth/me', { Authorization: `Bearer ${token}` });
    return res.user;
  }
}
