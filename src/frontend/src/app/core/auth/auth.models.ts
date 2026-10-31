export interface LoginRequest {
  readonly identifier: string;
  readonly password: string;
}

export interface RegisterWithEmailRequest {
  readonly email: string;
}
export interface AcceptedMessage {
  readonly message: string;
}
export interface ConfirmEmailEnrollmentRequest {
  readonly challengeId: string;
  readonly token: string;
  readonly password: string;
  readonly firstName: string;
  readonly lastName: string;
}
export interface ReauthenticationProof {
  readonly proof: string;
  readonly expiresAt: string;
}
export interface SignInMethods {
  readonly hasPassword: boolean;
}

export interface RequestPasswordResetRequest {
  readonly email: string;
}

export interface ResetPasswordRequest {
  readonly email: string;
  readonly token: string;
  readonly newPassword: string;
}

export interface RefreshTokenRequest {
  readonly refreshToken: string;
  /** The shop the expiring token was scoped to, so renewal stays in that shop when it still can. */
  readonly activeShopId?: string | null;
}

export interface AuthUser {
  readonly id: string;
  readonly email: string | null;
  readonly phoneNumber: string | null;
  readonly firstName: string;
  readonly lastName: string;
  readonly language?: string;
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
  readonly code?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly errors?: Record<string, string[]>;
}

export type BootstrapSessionStatus =
  | 'READY'
  | 'UNAUTHENTICATED'
  | 'API_UNREACHABLE'
  | 'REFRESH_FAILED';
