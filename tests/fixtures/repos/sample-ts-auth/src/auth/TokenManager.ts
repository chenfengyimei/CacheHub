import { TokenPair } from './types';

export class TokenManager {
  private accessToken: string | null = null;
  private refreshToken: string | null = null;
  private expiresAt: number = 0;

  setTokens(tokens: TokenPair): void {
    this.accessToken = tokens.accessToken;
    this.refreshToken = tokens.refreshToken;
    this.expiresAt = Date.now() + (tokens.expiresIn * 1000);
  }

  getAccessToken(): string | null {
    if (this.isExpired()) return null;
    return this.accessToken;
  }

  getRefreshToken(): string | null {
    return this.refreshToken;
  }

  isExpired(): boolean {
    return Date.now() >= this.expiresAt;
  }

  clearTokens(): void {
    this.accessToken = null;
    this.refreshToken = null;
    this.expiresAt = 0;
  }
}
