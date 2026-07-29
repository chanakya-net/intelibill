import { expect, test } from '@playwright/test';

import { AUTH_ENDPOINTS } from '../../../src/app/core/auth/auth.constants';
import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import {
  mockAuthenticatedShellRequests,
  mockExternalCallbackDelayed,
  mockExternalCallbackError,
  mockExternalCallbackSuccess,
  mockForgotPasswordDelayed,
  mockForgotPasswordError,
  mockForgotPasswordRateLimited,
  mockForgotPasswordSuccess,
  mockLoginDelayed,
  mockLoginError,
  mockLoginSuccess,
  mockLongTranslation,
  mockRegisterDelayed,
  mockRegisterError,
  mockRegisterSuccess,
  mockResetPasswordDelayed,
  mockResetPasswordInvalidToken,
  mockResetPasswordRateLimited,
  mockResetPasswordSuccess,
} from '../fixtures/auth.fixture';
import { AUDIT_VIEWPORTS } from '../support/layout-assertions';
import {
  assertActiveLocale,
  assertAuthenticatedDestination,
  assertAuthLayout,
  assertCallbackLayout,
  auditPublicPage,
  expectedError,
  fillRegisterForm,
  fillResetForm,
  isolatedLocalePage,
} from './public-pages.helpers';

const RESET_QUERY = '?email=audit%40example.com&token=valid-token';
const AUTH_ROUTES = ['/login', '/register', '/forgot-password', `/reset-password${RESET_QUERY}`] as const;
const CALLBACK_BUSY_COPY = {
  'en-IN': 'Finishing external authentication.',
  'hi-IN': 'बाहरी प्रमाणीकरण समाप्त कर रहा है।',
  'ta-IN': 'வெளிப்புற அங்கீகாரத்தை முடிக்கிறது.',
  'te-IN': 'బాహ్య ప్రామాణీకరణను ముగిస్తోంది.',
  'bn-IN': 'বাহ্যিক প্রমাণীকরণ সমাপ্ত হচ্ছে।',
  'ml-IN': 'ബാഹ്യ ആധികാരികത പൂർത്തിയാക്കുന്നു.',
  'kn-IN': 'ಬಾಹ್ಯ ದೃಢೀಕರಣವನ್ನು ಪೂರ್ಣಗೊಳಿಸಲಾಗುತ್ತಿದೆ.',
  'mr-IN': 'बाह्य प्रमाणीकरण पूर्ण होत आहे.',
  'gu-IN': 'બાહ્ય પ્રમાણીકરણ પૂર્ણ થઈ રહ્યું છે.',
} as const;

function viewportFor(projectName: string) {
  return projectName.includes('mobile') ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];
}

async function fillLoginForm(page: import('@playwright/test').Page, password = 'correct-horse-battery'): Promise<void> {
  await page.locator('#identifier').fill('audit@example.com');
  await page.locator('#password').fill(password);
}

async function fillEmail(page: import('@playwright/test').Page): Promise<void> {
  await page.locator('#email').fill('audit@example.com');
}

test.describe('public-pages: login', () => {
  test('renders default layout', async ({ page }, info) => {
    await auditPublicPage(page, '/login', undefined, async (current) => {
      await assertAuthLayout(current, viewportFor(info.project.name));
      await expect(current.locator('#identifier')).toBeVisible();
    });
  });

  test('shows validation errors', async ({ page }) => {
    await auditPublicPage(page, '/login', undefined, async (current) => {
      await current.locator('button[type="submit"]').click();
      await expect(current.locator('.field-error').first()).toBeVisible();
    });
  });

  test('shows submitting state', async ({ page }) => {
    await auditPublicPage(page, '/login', mockLoginDelayed, async (current) => {
      await fillLoginForm(current);
      await current.locator('button[type="submit"]').click();
      await expect(current.locator('.form-overlay')).toBeVisible();
    });
  });

  test('shows server error', async ({ page }) => {
    await auditPublicPage(page, '/login', mockLoginError, async (current) => {
      await fillLoginForm(current, 'wrong-password');
      await current.locator('button[type="submit"]').click();
      await expect(current.getByText('The email or password is incorrect.')).toBeVisible();
    }, expectedError(AUTH_ENDPOINTS.login, 401));
  });

  test('shows reset success feedback', async ({ page }) => {
    await auditPublicPage(page, '/login?passwordReset=success', undefined, async (current) => {
      await expect(current.getByText('Your password has been reset. Please sign in with your new password.')).toBeVisible();
    });
  });

  test('reaches the expected protected destination', async ({ page }) => {
    await auditPublicPage(page, '/login', async (current) => {
      await mockAuthenticatedShellRequests(current);
      await mockLoginSuccess(current);
    }, async (current) => {
      await fillLoginForm(current);
      await current.locator('button[type="submit"]').click();
      await assertAuthenticatedDestination(current);
    });
  });
});

test.describe('public-pages: register', () => {
  test('renders default layout', async ({ page }, info) => {
    await auditPublicPage(page, '/register', undefined, (current) => assertAuthLayout(current, viewportFor(info.project.name)));
  });

  test('shows validation errors', async ({ page }) => {
    await auditPublicPage(page, '/register', undefined, async (current) => {
      await current.locator('#email').fill('not-an-email');
      await current.locator('#password').fill('short');
      await current.locator('#confirmPassword').fill('different');
      await current.locator('button[type="submit"]').click();
      await expect(current.getByText('Enter a valid email address.')).toBeVisible();
    });
  });

  test('shows submitting state', async ({ page }) => {
    await auditPublicPage(page, '/register', mockRegisterDelayed, async (current) => {
      await fillRegisterForm(current);
      await current.locator('button[type="submit"]').click();
      await expect(current.locator('.form-overlay')).toBeVisible();
    });
  });

  test('shows server error', async ({ page }) => {
    await auditPublicPage(page, '/register', mockRegisterError, async (current) => {
      await fillRegisterForm(current);
      await current.locator('button[type="submit"]').click();
      await expect(current.getByText('An account with this email already exists.')).toBeVisible();
    }, expectedError(AUTH_ENDPOINTS.registerWithEmail, 409));
  });

  test('reaches the expected protected destination', async ({ page }) => {
    await auditPublicPage(page, '/register', async (current) => {
      await mockAuthenticatedShellRequests(current);
      await mockRegisterSuccess(current);
    }, async (current) => {
      await fillRegisterForm(current);
      await current.locator('button[type="submit"]').click();
      await assertAuthenticatedDestination(current);
    });
  });
});

test.describe('public-pages: forgot password', () => {
  test('renders default layout', async ({ page }, info) => {
    await auditPublicPage(page, '/forgot-password', undefined, (current) => assertAuthLayout(current, viewportFor(info.project.name)));
  });

  test('shows validation errors', async ({ page }) => {
    await auditPublicPage(page, '/forgot-password', undefined, async (current) => {
      await current.locator('#email').fill('not-an-email');
      await current.locator('button[type="submit"]').click();
      await expect(current.getByText('Enter a valid email address.')).toBeVisible();
    });
  });

  test('shows submitting state', async ({ page }) => {
    await auditPublicPage(page, '/forgot-password', mockForgotPasswordDelayed, async (current) => {
      await fillEmail(current);
      await current.locator('button[type="submit"]').click();
      await expect(current.locator('form.form-busy')).toBeVisible();
      await expect(current.locator('button[type="submit"]')).toBeDisabled();
    });
  });

  test('shows success feedback', async ({ page }) => {
    await auditPublicPage(page, '/forgot-password', mockForgotPasswordSuccess, async (current) => {
      await fillEmail(current);
      await current.locator('button[type="submit"]').click();
      await expect(current.getByText('We will send you a password reset link if your email is found in our records.')).toBeVisible();
    });
  });

  for (const [name, mock, message, status] of [
    ['rate-limited', mockForgotPasswordRateLimited, 'Too many attempts. Please try again later.', 429],
    ['server', mockForgotPasswordError, 'Unable to process your request. Please try again.', 500],
  ] as const) {
    test(`shows ${name} error feedback`, async ({ page }) => {
      await auditPublicPage(page, '/forgot-password', mock, async (current) => {
        await fillEmail(current);
        await current.locator('button[type="submit"]').click();
        await expect(current.getByText(message)).toBeVisible();
      }, expectedError(AUTH_ENDPOINTS.requestPasswordReset, status));
    });
  }
});

test.describe('public-pages: reset password', () => {
  test('renders form and invalid-link state', async ({ page }, info) => {
    await auditPublicPage(page, `/reset-password${RESET_QUERY}`, undefined, (current) => assertAuthLayout(current, viewportFor(info.project.name)));
    await auditPublicPage(page, '/reset-password', undefined, async (current) => {
      await expect(current.getByText('This password reset link is invalid or has expired.')).toBeVisible();
    });
  });

  test('shows validation errors', async ({ page }) => {
    await auditPublicPage(page, `/reset-password${RESET_QUERY}`, undefined, async (current) => {
      await current.locator('#password').fill('longenough1');
      await current.locator('#confirmPassword').fill('different1');
      await current.locator('button[type="submit"]').click();
      await expect(current.getByText('Passwords do not match.')).toBeVisible();
    });
  });

  test('shows submitting state', async ({ page }) => {
    await auditPublicPage(page, `/reset-password${RESET_QUERY}`, mockResetPasswordDelayed, async (current) => {
      await fillResetForm(current);
      await current.locator('button[type="submit"]').click();
      await expect(current.locator('.form-overlay')).toBeVisible();
    });
  });

  test('returns to login with success feedback', async ({ page }) => {
    await auditPublicPage(page, `/reset-password${RESET_QUERY}`, mockResetPasswordSuccess, async (current) => {
      await fillResetForm(current);
      await current.locator('button[type="submit"]').click();
      await current.waitForURL('/login?passwordReset=success');
      await expect(current.getByText('Your password has been reset. Please sign in with your new password.')).toBeVisible();
    });
  });

  for (const [name, mock, message, status] of [
    ['invalid-token', mockResetPasswordInvalidToken, 'This password reset link is invalid or has expired.', 401],
    ['rate-limited', mockResetPasswordRateLimited, 'Too many attempts. Please try again later.', 429],
  ] as const) {
    test(`shows ${name} error feedback`, async ({ page }) => {
      await auditPublicPage(page, `/reset-password${RESET_QUERY}`, mock, async (current) => {
        await fillResetForm(current);
        await current.locator('button[type="submit"]').click();
        await expect(current.getByText(message)).toBeVisible();
      }, expectedError(AUTH_ENDPOINTS.confirmPasswordReset, status));
    });
  }
});

test.describe('public-pages: auth callback', () => {
  test('shows busy spinner and copy while exchanging', async ({ page }) => {
    await auditPublicPage(page, '/auth/callback?code=auth-code&state=auth-state', mockExternalCallbackDelayed, async (current) => {
      await expect(current.getByText('Finishing external authentication.')).toBeVisible();
      await expect(current.locator('.p-progressspinner')).toBeVisible();
    });
  });

  test('reaches expected protected destination', async ({ page }) => {
    await auditPublicPage(page, '/auth/callback?code=auth-code&state=auth-state', async (current) => {
      await mockAuthenticatedShellRequests(current);
      await mockExternalCallbackSuccess(current);
    }, assertAuthenticatedDestination);
  });

  for (const [name, url, configure, message, expected] of [
    ['provider rejection', '/auth/callback?error=access_denied', undefined, 'External provider returned an error. Please try again.', undefined],
    ['missing callback data', '/auth/callback', undefined, 'Missing callback code or state. Please retry sign-in.', undefined],
    ['rejected exchange', '/auth/callback?code=auth-code&state=auth-state', mockExternalCallbackError, 'Your sign-in session expired. Please try again.', expectedError(AUTH_ENDPOINTS.loginExternalCallback, 401)],
  ] as const) {
    test(`shows visible feedback for ${name}`, async ({ page }) => {
      await auditPublicPage(page, url, configure, async (current) => {
        await current.waitForURL(/\/login\?externalAuthError=/);
        const feedback = current.getByText(message);
        await expect(feedback).toBeVisible();
        await expect(feedback).toBeInViewport();
      }, expected);
    });
  }
});

test.describe('public-pages: locales and wildcard', () => {
  test('uses isolated locale setup, selected language, and long translations', async ({ page }, info) => {
    const viewport = viewportFor(info.project.name);
    for (const language of SUPPORTED_LANGUAGES) {
      for (const route of AUTH_ROUTES) {
        const localizedPage = await isolatedLocalePage(page.context(), language);
        try {
          await auditPublicPage(localizedPage, route, undefined, async (current) => {
            await assertActiveLocale(current, language);
            await assertAuthLayout(current, viewport);
          });
        } finally {
          await localizedPage.close();
        }
      }

      const wildcardPage = await isolatedLocalePage(page.context(), language);
      try {
        await auditPublicPage(wildcardPage, '/this-route-does-not-exist', undefined, async (current) => {
          await expect(current.locator('h2')).not.toHaveText('notFound.title');
          const width = await current.evaluate(() => document.documentElement.scrollWidth);
          expect(width).toBeLessThanOrEqual(viewport.width + 1);
        });
      } finally {
        await wildcardPage.close();
      }
    }

    const longLabelPage = await isolatedLocalePage(page.context(), 'hi-IN');
    try {
      await auditPublicPage(longLabelPage, '/login', async (current) => {
        await mockLongTranslation(current, 'hi-IN');
      }, async (current) => {
        await expect(current.locator('.auth-card h2')).toHaveText(/Sign in to continue managing every detail/);
        await assertAuthLayout(current, viewport);
      });
    } finally {
      await longLabelPage.close();
    }
  });

  test('renders localized callback copy within bounds', async ({ page }, info) => {
    const viewport = viewportFor(info.project.name);
    for (const language of SUPPORTED_LANGUAGES) {
      const callbackPage = await isolatedLocalePage(page.context(), language);
      try {
        await auditPublicPage(
          callbackPage,
          '/auth/callback?code=auth-code&state=auth-state',
          mockExternalCallbackDelayed,
          async (current) => {
            await expect(current.getByText(CALLBACK_BUSY_COPY[language])).toBeVisible();
            await assertCallbackLayout(current, viewport);
          },
        );
      } finally {
        await callbackPage.close();
      }
    }
  });

  test('renders wildcard route without failures or overflow', async ({ page }, info) => {
    await auditPublicPage(page, '/this-route-does-not-exist', undefined, async (current) => {
      await expect(current.getByText('Page Not Found')).toBeVisible();
      await expect(current.getByRole('link', { name: 'Go Back' })).toBeVisible();
      const width = await current.evaluate(() => document.documentElement.scrollWidth);
      expect(width).toBeLessThanOrEqual(viewportFor(info.project.name).width + 1);
    });
  });
});
