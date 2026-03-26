export interface LoginWithEmailRequest {
  readonly email: string;
  readonly password: string;
}

export interface RefreshTokenRequest {
  readonly refreshToken: string;
}

export interface AuthUser {
  readonly id: string;
  readonly email: string | null;
  readonly phoneNumber: string | null;
  readonly firstName: string;
  readonly lastName: string;
}

export interface AuthResult {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly accessTokenExpiresAt: string;
  readonly refreshTokenExpiresAt: string;
  readonly user: AuthUser;
}

export interface AuthSession {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly accessTokenExpiresAt: string;
  readonly refreshTokenExpiresAt: string;
  readonly rememberMe: boolean;
  readonly user: AuthUser;
}

export interface ApiErrorPayload {
  readonly title?: string;
  readonly detail?: string;
  readonly errors?: Record<string, string[]>;
}
