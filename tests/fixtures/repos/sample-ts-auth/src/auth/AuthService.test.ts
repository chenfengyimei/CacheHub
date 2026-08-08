import { AuthService } from './AuthService';
import { TokenManager } from './TokenManager';
import { HttpClient } from '../config/http';

describe('AuthService', () => {
  let http: HttpClient;
  let tokenManager: TokenManager;
  let authService: AuthService;

  beforeEach(() => {
    http = new HttpClient();
    tokenManager = new TokenManager();
    authService = new AuthService(http, tokenManager);
  });

  test('login sets tokens', async () => {
    const mockPost = jest.spyOn(http, 'post').mockResolvedValue({
      user: { id: '1', email: 'test@test.com', name: 'Test', role: 'user' },
      tokens: { accessToken: 'acc', refreshToken: 'ref', expiresIn: 3600 },
    });

    const user = await authService.login({ email: 'test@test.com', password: 'pass' });
    expect(user.email).toBe('test@test.com');
    expect(mockPost).toHaveBeenCalledWith('/auth/login', { email: 'test@test.com', password: 'pass' });
  });

  test('refreshToken returns new tokens', async () => {
    tokenManager.setTokens({ accessToken: 'old', refreshToken: 'ref', expiresIn: 0 });
    jest.spyOn(http, 'post').mockResolvedValue({
      tokens: { accessToken: 'new', refreshToken: 'newref', expiresIn: 3600 },
    });

    const tokens = await authService.refreshToken();
    expect(tokens.accessToken).toBe('new');
  });

  test('logout clears tokens', async () => {
    tokenManager.setTokens({ accessToken: 'acc', refreshToken: 'ref', expiresIn: 3600 });
    jest.spyOn(http, 'post').mockResolvedValue({});
    await authService.logout();
    expect(tokenManager.getAccessToken()).toBeNull();
  });

  test('getProfile throws when not authenticated', async () => {
    await expect(authService.getProfile()).rejects.toThrow('Not authenticated');
  });
});
