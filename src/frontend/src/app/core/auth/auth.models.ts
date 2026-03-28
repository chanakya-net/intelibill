export interface LoginWithEmailRequest {
  readonly email: string;
  readonly password: string;
}

export interface RegisterWithEmailRequest {
  readonly email: string;
  readonly password: string;
  readonly firstName: string;
  readonly lastName: string;
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

export interface UserShop {
  readonly shopId: string;
  readonly shopName: string;
  readonly role: string;
  readonly isDefault: boolean;
  readonly lastUsedAt: string | null;
}

export interface AuthResult {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly accessTokenExpiresAt: string;
  readonly refreshTokenExpiresAt: string;
  readonly user: AuthUser;
  readonly activeShopId: string | null;
  readonly shops: readonly UserShop[];
}

export interface AuthSession {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly accessTokenExpiresAt: string;
  readonly refreshTokenExpiresAt: string;
  readonly rememberMe: boolean;
  readonly user: AuthUser;
  readonly activeShopId: string | null;
  readonly shops: readonly UserShop[];
}

export interface ApiErrorPayload {
  readonly title?: string;
  readonly detail?: string;
  readonly errors?: Record<string, string[]>;
}
