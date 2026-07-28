import { expect, test } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import {
  mockExternalCallbackError,
  mockExternalCallbackSuccess,
  mockForgotPasswordError,
  mockForgotPasswordRateLimited,
  mockForgotPasswordSuccess,
  mockLoginDelayed,
  mockLoginError,
  mockLoginSuccess,
  mockRegisterDelayed,
  mockRegisterError,
  mockRegisterSuccess,
  mockResetPasswordInvalidToken,
  mockResetPasswordRateLimited,
  mockResetPasswordSuccess,
  setStoredLanguage,
} from '../fixtures/auth.fixture';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';
import { AUDIT_VIEWPORTS, assertLoginLayout } from '../support/layout-assertions';

function getAuditViewport(projectName: string) {
  return projectName.includes('mobile') ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];
}

async function assertNoHorizontalOverflow(page: import('@playwright/test').Page, width: number): Promise<void> {
  const documentWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(documentWidth).toBeLessThanOrEqual(width + 1);
}

async function waitForAuthNavigationAway(page: import('@playwright/test').Page): Promise<void> {
  await page.waitForURL((url) => !['/login', '/register', '/auth/callback'].includes(url.pathname));
}

test.describe('public-pages: login', () => {
  test('renders default state without failures', async ({ page }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/login');
      await waitForStablePage(page);

      await assertLoginLayout(page, getAuditViewport(testInfo.project.name));
      await expect(page.locator('.auth-card')).toBeVisible();
      await expect(page.locator('#identifier')).toBeVisible();
      await expect(page.locator('#password')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows validation errors for empty required fields', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/login');
      await waitForStablePage(page);

      await page.locator('button[type="submit"]').click();

      await expect(page.locator('.field-error').first()).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows loading state while submitting', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockLoginDelayed(page);
      await page.goto('/login');
      await waitForStablePage(page);

      await page.locator('#identifier').fill('audit@example.com');
      await page.locator('#password').fill('correct-horse-battery');
      await page.locator('button[type="submit"]').click();

      await expect(page.locator('.form-overlay')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows server error message on invalid credentials', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockLoginError(page);
      await page.goto('/login');
      await waitForStablePage(page);

      await page.locator('#identifier').fill('audit@example.com');
      await page.locator('#password').fill('wrong-password');
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('The email or password is incorrect.')).toBeVisible();
    } finally {
      collector.dispose();
    }
  });

  test('navigates away on successful login', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockLoginSuccess(page);
      await page.goto('/login');
      await waitForStablePage(page);

      await page.locator('#identifier').fill('audit@example.com');
      await page.locator('#password').fill('correct-horse-battery');
      await page.locator('button[type="submit"]').click();

      await waitForAuthNavigationAway(page);
    } finally {
      collector.dispose();
    }
  });

  test('shows password reset success banner from query param', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/login?passwordReset=success');
      await waitForStablePage(page);

      await expect(
        page.getByText('Your password has been reset. Please sign in with your new password.'),
      ).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders across every supported locale without overflow', async ({ page }, testInfo) => {
    const localeViewport =
      testInfo.project.name === 'chromium-mobile' ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];

    for (const language of SUPPORTED_LANGUAGES) {
      const collector = collectBrowserFailures(page);
      try {
        await setStoredLanguage(page, language);
        await mockExternalRequests(page);
        await page.goto('/login');
        await waitForStablePage(page);

        await assertLoginLayout(page, localeViewport);
        await expect(page.locator('.auth-card h2')).not.toHaveText('auth.loginNow');
        assertNoUnexpectedBrowserFailures(collector.failures);
      } finally {
        collector.dispose();
      }
    }
  });
});

test.describe('public-pages: register', () => {
  test('renders default state without failures', async ({ page }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/register');
      await waitForStablePage(page);

      await assertNoHorizontalOverflow(page, getAuditViewport(testInfo.project.name).width);
      await expect(page.locator('#email')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows validation errors for invalid fields', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/register');
      await waitForStablePage(page);

      await page.locator('#email').fill('not-an-email');
      await page.locator('#password').fill('short');
      await page.locator('#confirmPassword').fill('different');
      await page.locator('#email').blur();
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('Enter a valid email address.')).toBeVisible();
      await expect(page.getByText('Password must be between 8 and 100 characters.')).toBeVisible();
      await expect(page.getByText('Passwords do not match.')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows loading state while submitting', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockRegisterDelayed(page);
      await page.goto('/register');
      await waitForStablePage(page);

      await fillValidRegisterForm(page);
      await page.locator('button[type="submit"]').click();

      await expect(page.locator('.form-overlay')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows server error message when email already registered', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockRegisterError(page);
      await page.goto('/register');
      await waitForStablePage(page);

      await fillValidRegisterForm(page);
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('An account with this email already exists.')).toBeVisible();
    } finally {
      collector.dispose();
    }
  });

  test('navigates away on successful registration', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockRegisterSuccess(page);
      await page.goto('/register');
      await waitForStablePage(page);

      await fillValidRegisterForm(page);
      await page.locator('button[type="submit"]').click();

      await waitForAuthNavigationAway(page);
    } finally {
      collector.dispose();
    }
  });

  test('renders across every supported locale without overflow', async ({ page }, testInfo) => {
    const localeViewport =
      testInfo.project.name === 'chromium-mobile' ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];

    for (const language of SUPPORTED_LANGUAGES) {
      const collector = collectBrowserFailures(page);
      try {
        await setStoredLanguage(page, language);
        await mockExternalRequests(page);
        await page.goto('/register');
        await waitForStablePage(page);

        await assertNoHorizontalOverflow(page, localeViewport.width);
        assertNoUnexpectedBrowserFailures(collector.failures);
      } finally {
        collector.dispose();
      }
    }
  });
});

test.describe('public-pages: forgot-password', () => {
  test('renders default state without failures', async ({ page }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/forgot-password');
      await waitForStablePage(page);

      await assertLoginLayout(page, getAuditViewport(testInfo.project.name));
      await expect(page.locator('#email')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows validation error for invalid email', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/forgot-password');
      await waitForStablePage(page);

      await page.locator('#email').fill('not-an-email');
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('Enter a valid email address.')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows success message after request submitted', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockForgotPasswordSuccess(page);
      await page.goto('/forgot-password');
      await waitForStablePage(page);

      await page.locator('#email').fill('audit@example.com');
      await page.locator('button[type="submit"]').click();

      await expect(
        page.getByText('We will send you a password reset link if your email is found in our records.'),
      ).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows rate-limit error message', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockForgotPasswordRateLimited(page);
      await page.goto('/forgot-password');
      await waitForStablePage(page);

      await page.locator('#email').fill('audit@example.com');
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('Too many attempts. Please try again later.')).toBeVisible();
    } finally {
      collector.dispose();
    }
  });

  test('shows generic error message on server failure', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockForgotPasswordError(page);
      await page.goto('/forgot-password');
      await waitForStablePage(page);

      await page.locator('#email').fill('audit@example.com');
      await page.locator('button[type="submit"]').click();

      await expect(
        page.getByText('Unable to process your request. Please try again.'),
      ).toBeVisible();
    } finally {
      collector.dispose();
    }
  });

  test('renders across every supported locale without overflow', async ({ page }, testInfo) => {
    const localeViewport =
      testInfo.project.name === 'chromium-mobile' ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];

    for (const language of SUPPORTED_LANGUAGES) {
      const collector = collectBrowserFailures(page);
      try {
        await setStoredLanguage(page, language);
        await mockExternalRequests(page);
        await page.goto('/forgot-password');
        await waitForStablePage(page);

        await assertLoginLayout(page, localeViewport);
        assertNoUnexpectedBrowserFailures(collector.failures);
      } finally {
        collector.dispose();
      }
    }
  });
});

test.describe('public-pages: reset-password', () => {
  const validQuery = '?email=audit%40example.com&token=valid-token';

  test('renders form when link parameters are present', async ({ page }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto(`/reset-password${validQuery}`);
      await waitForStablePage(page);

      await assertLoginLayout(page, getAuditViewport(testInfo.project.name));
      await expect(page.locator('#password')).toBeVisible();
      await expect(page.locator('#confirmPassword')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows invalid-link message when parameters are missing', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/reset-password');
      await waitForStablePage(page);

      await expect(
        page.getByText('This password reset link is invalid or has expired.'),
      ).toBeVisible();
      await expect(page.locator('#password')).toHaveCount(0);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows validation error on password mismatch', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto(`/reset-password${validQuery}`);
      await waitForStablePage(page);

      await page.locator('#password').fill('longenough1');
      await page.locator('#confirmPassword').fill('different1');
      await page.locator('#confirmPassword').blur();
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('Passwords do not match.')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('redirects to login with success banner on submit', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockResetPasswordSuccess(page);
      await page.goto(`/reset-password${validQuery}`);
      await waitForStablePage(page);

      await page.locator('#password').fill('longenough1');
      await page.locator('#confirmPassword').fill('longenough1');
      await page.locator('button[type="submit"]').click();

      await page.waitForURL('/login?passwordReset=success');
      await waitForStablePage(page);
      await expect(
        page.getByText('Your password has been reset. Please sign in with your new password.'),
      ).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows invalid-link message when the reset token is rejected', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockResetPasswordInvalidToken(page);
      await page.goto(`/reset-password${validQuery}`);
      await waitForStablePage(page);

      await page.locator('#password').fill('longenough1');
      await page.locator('#confirmPassword').fill('longenough1');
      await page.locator('button[type="submit"]').click();

      await expect(
        page.getByText('This password reset link is invalid or has expired.'),
      ).toBeVisible();
      await expect(page.locator('#password')).toHaveCount(0);
    } finally {
      collector.dispose();
    }
  });

  test('shows rate-limit error message', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockResetPasswordRateLimited(page);
      await page.goto(`/reset-password${validQuery}`);
      await waitForStablePage(page);

      await page.locator('#password').fill('longenough1');
      await page.locator('#confirmPassword').fill('longenough1');
      await page.locator('button[type="submit"]').click();

      await expect(page.getByText('Too many attempts. Please try again later.')).toBeVisible();
    } finally {
      collector.dispose();
    }
  });

  test('renders across every supported locale without overflow', async ({ page }, testInfo) => {
    const localeViewport =
      testInfo.project.name === 'chromium-mobile' ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];

    for (const language of SUPPORTED_LANGUAGES) {
      const collector = collectBrowserFailures(page);
      try {
        await setStoredLanguage(page, language);
        await mockExternalRequests(page);
        await page.goto(`/reset-password${validQuery}`);
        await waitForStablePage(page);

        await assertLoginLayout(page, localeViewport);
        assertNoUnexpectedBrowserFailures(collector.failures);
      } finally {
        collector.dispose();
      }
    }
  });
});

test.describe('public-pages: auth callback', () => {
  test('navigates away on successful callback', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockExternalCallbackSuccess(page);
      await page.goto('/auth/callback?code=auth-code&state=auth-state');

      await waitForAuthNavigationAway(page);
    } finally {
      collector.dispose();
    }
  });

  test('shows error and returns to login when the provider reports an error', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/auth/callback?error=access_denied');
      await waitForStablePage(page);

      await page.waitForURL(/\/login\?externalAuthError=/);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows error when callback parameters are missing', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/auth/callback');
      await waitForStablePage(page);

      await page.waitForURL(/\/login\?externalAuthError=/);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('shows error when the callback exchange is rejected', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await mockExternalCallbackError(page);
      await page.goto('/auth/callback?code=auth-code&state=auth-state');
      await waitForStablePage(page);

      await page.waitForURL(/\/login\?externalAuthError=/);
    } finally {
      collector.dispose();
    }
  });
});

test.describe('public-pages: wildcard 404', () => {
  test('renders not-found page for unknown routes without failures', async ({ page }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page);
      await page.goto('/this-route-does-not-exist');
      await waitForStablePage(page);

      await expect(page.getByText('Page Not Found')).toBeVisible();
      await expect(page.getByRole('link', { name: 'Go Back' })).toBeVisible();

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth);
      expect(overflow).toBeLessThanOrEqual(getAuditViewport(testInfo.project.name).width + 1);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders across every supported locale without overflow', async ({ page }, testInfo) => {
    const localeViewport =
      testInfo.project.name === 'chromium-mobile' ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];

    for (const language of SUPPORTED_LANGUAGES) {
      const collector = collectBrowserFailures(page);
      try {
        await setStoredLanguage(page, language);
        await mockExternalRequests(page);
        await page.goto('/this-route-does-not-exist');
        await waitForStablePage(page);

        const overflow = await page.evaluate(() => document.documentElement.scrollWidth);
        expect(overflow).toBeLessThanOrEqual(localeViewport.width + 1);
        assertNoUnexpectedBrowserFailures(collector.failures);
      } finally {
        collector.dispose();
      }
    }
  });
});

async function fillValidRegisterForm(page: import('@playwright/test').Page): Promise<void> {
  await page.locator('#firstName').fill('Audit');
  await page.locator('#lastName').fill('User');
  await page.locator('#email').fill('audit@example.com');
  await page.locator('#phoneNumber').fill('9800000000');
  await page.locator('#password').fill('longenough1');
  await page.locator('#confirmPassword').fill('longenough1');
}
