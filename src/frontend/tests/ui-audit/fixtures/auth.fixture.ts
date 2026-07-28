import type { Page } from '@playwright/test';

import { AUTH_ENDPOINTS } from '../../../src/app/core/auth/auth.constants';
import type {
  ApiErrorPayload,
  AuthResult,
  AuthUser,
} from '../../../src/app/core/auth/auth.models';
import { SupportedLanguage } from '../../../src/app/core/i18n/language.constants';

const LANGUAGE_STORAGE_KEY = 'inventory.preferences.language';

export const MOCK_AUTH_USER: AuthUser = {
  id: 'ui-audit-user-001',
  email: 'ui-audit@example.com',
  phoneNumber: '+919800000000',
  firstName: 'Audit',
  lastName: 'User',
  language: 'en-IN',
};

export const MOCK_AUTH_RESULT: AuthResult = {
  accessToken: 'ui-audit-access-token',
  refreshToken: 'ui-audit-refresh-token',
  accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  refreshTokenExpiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
  user: MOCK_AUTH_USER,
  activeShopId: null,
  shops: [],
};

export interface MockEndpointOptions {
  readonly status: number;
  readonly body: unknown;
  readonly delayMs?: number;
}

async function mockEndpoint(page: Page, url: string, options: MockEndpointOptions): Promise<void> {
  await page.route(url, async (route) => {
    if (options.delayMs) {
      await new Promise((resolve) => setTimeout(resolve, options.delayMs));
    }

    await route.fulfill({
      status: options.status,
      contentType: 'application/json',
      body: JSON.stringify(options.body),
    });
  });
}

export function mockLoginSuccess(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.login, { status: 200, body: MOCK_AUTH_RESULT });
}

export function mockLoginDelayed(page: Page, delayMs = 1500): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.login, {
    status: 200,
    body: MOCK_AUTH_RESULT,
    delayMs,
  });
}

export function mockLoginError(page: Page): Promise<void> {
  const error: ApiErrorPayload = { title: 'Auth.InvalidCredentials' };
  return mockEndpoint(page, AUTH_ENDPOINTS.login, { status: 401, body: error });
}

export function mockRegisterSuccess(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.registerWithEmail, {
    status: 200,
    body: MOCK_AUTH_RESULT,
  });
}

export function mockRegisterDelayed(page: Page, delayMs = 1500): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.registerWithEmail, {
    status: 200,
    body: MOCK_AUTH_RESULT,
    delayMs,
  });
}

export function mockRegisterError(page: Page): Promise<void> {
  const error: ApiErrorPayload = { title: 'Auth.EmailAlreadyInUse' };
  return mockEndpoint(page, AUTH_ENDPOINTS.registerWithEmail, { status: 409, body: error });
}

export function mockForgotPasswordSuccess(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.requestPasswordReset, { status: 200, body: {} });
}

export function mockForgotPasswordDelayed(page: Page, delayMs = 1500): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.requestPasswordReset, {
    status: 200,
    body: {},
    delayMs,
  });
}

export function mockForgotPasswordRateLimited(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.requestPasswordReset, { status: 429, body: {} });
}

export function mockForgotPasswordError(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.requestPasswordReset, { status: 500, body: {} });
}

export function mockResetPasswordSuccess(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.confirmPasswordReset, { status: 200, body: {} });
}

export function mockResetPasswordDelayed(page: Page, delayMs = 1500): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.confirmPasswordReset, {
    status: 200,
    body: {},
    delayMs,
  });
}

export function mockResetPasswordInvalidToken(page: Page): Promise<void> {
  const error: ApiErrorPayload = { title: 'Auth.InvalidPasswordResetToken' };
  return mockEndpoint(page, AUTH_ENDPOINTS.confirmPasswordReset, { status: 401, body: error });
}

export function mockResetPasswordRateLimited(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.confirmPasswordReset, { status: 429, body: {} });
}

export function mockExternalCallbackSuccess(page: Page): Promise<void> {
  return mockEndpoint(page, AUTH_ENDPOINTS.loginExternalCallback, {
    status: 200,
    body: MOCK_AUTH_RESULT,
  });
}

export function mockExternalCallbackError(page: Page): Promise<void> {
  const error: ApiErrorPayload = { title: 'Auth.ExternalStateInvalid' };
  return mockEndpoint(page, AUTH_ENDPOINTS.loginExternalCallback, { status: 401, body: error });
}

export async function setStoredLanguage(page: Page, language: SupportedLanguage): Promise<void> {
  await page.addInitScript(
    ([key, value]) => {
      window.localStorage.setItem(key, value);
    },
    [LANGUAGE_STORAGE_KEY, language] as const,
  );
}
