import { expect, test } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

test.describe('offline-rendering', () => {
  test('renders login page with local fonts', async ({ page }, testInfo) => {
    const collector = collectBrowserFailures(page);

    try {
      await mockExternalRequests(page);
      await page.goto('/login');
      await waitForStablePage(page);

      await expect(page.locator('.auth-card')).toBeVisible();
      await expect(page.locator('#identifier')).toBeVisible();
      await expect(page.locator('#password')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('fonts are loaded from local assets', async ({ page }, testInfo) => {
    const fontRequests: string[] = [];
    const collector = collectBrowserFailures(page);

    try {
      await page.on('request', (request) => {
        const url = request.url();
        if (url.includes('/fonts/')) {
          fontRequests.push(url);
        }
      });

      await mockExternalRequests(page);
      await page.goto('/login');
      await waitForStablePage(page);

      expect(fontRequests.length).toBeGreaterThan(0);

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});
