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
export type PhonePurpose = 'enrollment' | 'login' | 'change';

/**
 * What the server tells a caller after queueing a code. It carries a masked destination and the
 * deadlines, never the code, and its shape is the same whether or not a code was actually sent —
 * an ineligible request comes back with an empty challengeId.
 */
export interface PhoneChallengeReceipt {
  readonly challengeId: string;
  readonly maskedDestination: string;
  readonly resendAllowedAt: string;
  readonly expiresAt: string;
}

export interface PhoneAuthAvailability {
  readonly enabled: boolean;
}

export interface ConfirmPhoneEnrollmentRequest {
  readonly challengeId: string;
  readonly code: string;
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
  /** This device's session, so it can recognise itself in its own session list. */
  readonly sessionId?: string | null;
}

/**
 * What a shop switch or a shop creation returns: a replacement access token for this device and no
 * new refresh secret. Choosing a shop is not a credential event, so it must not mint one.
 */
export interface AccessScopeResult {
  readonly accessToken: string;
  readonly accessTokenExpiresAt: string;
  readonly activeShopId: string | null;
  readonly scopeVersion: number;
  readonly shops: readonly UserShop[];
}

/**
 * What saving the default shop returns: the stored preference, with no token at all. This device
 * keeps the shop its operator is already working in.
 */
export interface ShopPreferenceResult {
  readonly activeShopId: string | null;
  readonly shops: readonly UserShop[];
}

/** One of the signed-in devices on this account, as shown to its owner. */
export interface DeviceSession {
  readonly sessionId: string;
  readonly deviceLabel: string | null;
  readonly userAgent: string | null;
  readonly ipAddress: string | null;
  readonly activeShopId: string | null;
  readonly createdAt: string;
  readonly lastSeenAt: string;
  readonly expiresAt: string;
  readonly isCurrent: boolean;
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
  readonly sessionId?: string | null;
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
