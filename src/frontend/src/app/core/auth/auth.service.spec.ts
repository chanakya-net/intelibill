import { describe, expect, it } from 'vitest';

import { AuthService, AuthSessionService, AuthTokenService } from './auth.service';

describe('AuthService barrel', () => {
  it('aliases AuthSessionService as AuthService', () => {
    expect(AuthService).toBe(AuthSessionService);
  });

  it('exports token service', () => {
    expect(AuthTokenService).toBeDefined();
  });
});
