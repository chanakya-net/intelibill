import { expect, test, type Locator, type Page } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
  type MockBankAccount,
} from '../support/audit-page';

const LONG_ACCOUNT: MockBankAccount = {
  id: 'long-1',
  bankName: 'The Very Long International Cooperative Banking Corporation of India',
  accountNumber: '12345678901234567890123456789012345678901234567890',
  accountType: 'Savings',
  ifscCode: 'LONG0001234',
  accountHolderName: 'Alexandria Cassandra Long Account Holder Name',
};

test.describe('bank-accounts', () => {
  test('renders an authenticated Owner list with account values', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openBankAccounts(page);
      await expect(page.locator('p-table')).toBeVisible();
      await expect(page.locator('tbody tr')).toHaveCount(2);
      await expect(page.locator('tbody')).toContainText('HDFC Bank');
      await expect(page.locator('tbody')).toContainText('1234567890123456');
      await expect(page.getByRole('button', { name: /add bank account/i })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders and asserts the empty state', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, returnEmptyAccounts: true });
      await visitBankAccounts(page);
      await expect(page.locator('.empty-state')).toBeVisible();
      await expect(page.locator('.empty-state h2')).toHaveText('No bank accounts found');
      await expect(page.locator('.empty-state p')).toHaveText(
        'Start by adding your first bank account.',
      );
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders loading and error states from explicit API responses', async ({ page }) => {
    const loadingCollector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, bankAccountsState: 'loading' });
      await page.goto('/bank-accounts');
      await expect(page.locator('.bank-accounts-page')).toBeVisible();
      await expect(page.locator('.loading')).toBeVisible();
      assertNoUnexpectedBrowserFailures(loadingCollector.failures);
    } finally {
      loadingCollector.dispose();
    }

    const errorCollector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().endsWith('/api/bank-accounts') && response.status() === 503,
    });
    try {
      await mockExternalRequests(page, { authenticated: true, bankAccountsState: 'error' });
      await visitBankAccounts(page);
      await expect(page.locator('.error')).toHaveText(
        'Unable to load bank accounts right now. Please try again.',
      );
      await expect(page.locator('.table-card')).toBeVisible();
      assertNoUnexpectedBrowserFailures(errorCollector.failures);
    } finally {
      errorCollector.dispose();
    }
  });

  test('requires valid fields, then submits add and edit CRUD workflows', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    let mutationRequests = 0;
    page.on('request', (request) => {
      if (request.url().includes('/api/bank-accounts') && request.method() !== 'GET')
        mutationRequests += 1;
    });

    try {
      await openBankAccounts(page);
      await page.getByRole('button', { name: /add bank account/i }).click();
      const overlay = page.locator('app-manage-bank-account-overlay .overlay');
      await expect(overlay).toBeVisible();
      await expect(overlay.locator('app-bank-account-form')).toBeVisible();

      await overlay.getByRole('button', { name: /save changes/i }).click();
      await expect(overlay.locator('input[formControlName="bankName"]')).toHaveClass(/ng-invalid/);
      await expect(overlay.locator('input[formControlName="accountNumber"]')).toHaveClass(
        /ng-invalid/,
      );
      expect(mutationRequests).toBe(0);

      const ifscInput = overlay.locator('input[formControlName="ifscCode"]');
      await ifscInput.fill('INVALID123');
      await ifscInput.blur();
      await expect(ifscInput).toHaveClass(/ng-invalid/);
      await expect(overlay.locator('.field-error')).toHaveText(/valid indian IFSC code/i);
      expect(mutationRequests).toBe(0);

      await fillAccountForm(page, overlay, {
        bankName: 'Axis Bank',
        accountNumber: '1111222233334444',
        accountHolderName: 'Audit Owner',
        ifscCode: 'UTIB0001234',
      });
      await overlay.getByRole('button', { name: /save changes/i }).click();
      await expect(overlay).toBeHidden();
      await expect(page.locator('tbody')).toContainText('Axis Bank');
      expect(mutationRequests).toBe(1);

      const createdRow = page.locator('tbody tr').filter({ hasText: 'Axis Bank' });
      await createdRow.locator('button').first().click();
      await expect(overlay).toBeVisible();
      await expect(overlay.locator('input[formControlName="bankName"]')).toHaveValue('Axis Bank');
      await expect(overlay.locator('input[formControlName="accountNumber"]')).toHaveValue(
        '1111222233334444',
      );

      await overlay.locator('input[formControlName="bankName"]').fill('Axis Bank Updated');
      await overlay.getByRole('button', { name: /save changes/i }).click();
      await expect(overlay).toBeHidden();
      await expect(page.locator('tbody')).toContainText('Axis Bank Updated');
      expect(mutationRequests).toBe(2);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps long values inside desktop, tablet, and mobile layouts', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, accounts: [LONG_ACCOUNT] });
      for (const viewport of [
        { width: 1920, height: 1080 },
        { width: 768, height: 1024 },
        { width: 375, height: 667 },
      ]) {
        await page.setViewportSize(viewport);
        await visitBankAccounts(page);
        await expect(page.locator('tbody')).toContainText(LONG_ACCOUNT.bankName);
        await assertNoHorizontalOverflow(page);
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('fits add and edit overlays in every supported locale and audit viewport', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-mobile', 'manual locale and overlay matrix');
    test.setTimeout(120_000);
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, accounts: [LONG_ACCOUNT] });
      for (const locale of SUPPORTED_LANGUAGES) {
        await page.addInitScript(
          ({ value }) => {
            localStorage.setItem('inventory.preferences.language', value);
          },
          { value: locale },
        );
        for (const viewport of [
          { width: 375, height: 667 },
          { width: 1440, height: 900 },
        ]) {
          await page.setViewportSize(viewport);
          await visitBankAccounts(page);
          const overlay = page.locator('app-manage-bank-account-overlay .overlay');
          await page.locator('.page-header button').click();
          await expect(overlay).toBeVisible();
          await assertOverlayFits(page, overlay);
          await overlay.locator('.close-button').click();
          await page.locator('tbody tr').first().locator('button').first().click();
          await expect(overlay).toBeVisible();
          await expect(overlay.locator('input[formControlName="bankName"]')).toHaveValue(
            LONG_ACCOUNT.bankName,
          );
          await assertOverlayFits(page, overlay);
          await overlay.locator('.close-button').click();
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function openBankAccounts(page: Page): Promise<void> {
  await mockExternalRequests(page, { authenticated: true });
  await visitBankAccounts(page);
}

async function visitBankAccounts(page: Page): Promise<void> {
  await page.goto('/bank-accounts');
  await waitForStablePage(page);
  await expect(page.locator('.bank-accounts-page')).toBeVisible();
}

async function fillAccountForm(
  page: Page,
  overlay: Locator,
  values: { bankName: string; accountNumber: string; accountHolderName: string; ifscCode: string },
): Promise<void> {
  await overlay.locator('input[formControlName="bankName"]').fill(values.bankName);
  await overlay.locator('input[formControlName="accountNumber"]').fill(values.accountNumber);
  await overlay.locator('input[formControlName="ifscCode"]').fill(values.ifscCode);
  await overlay
    .locator('input[formControlName="accountHolderName"]')
    .fill(values.accountHolderName);
  await overlay.locator('p-select').click();
  await page.getByRole('option', { name: 'Savings' }).click();
}

async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.viewport + 5);
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 5);
}

async function assertOverlayFits(page: Page, overlay: Locator): Promise<void> {
  const bounds = await overlay.locator('.overlay-card').evaluate((element) => {
    const box = element.getBoundingClientRect();
    return { left: box.left, right: box.right, top: box.top, bottom: box.bottom };
  });
  expect(bounds.left).toBeGreaterThanOrEqual(0);
  expect(bounds.right).toBeLessThanOrEqual(await page.evaluate(() => window.innerWidth));
  expect(bounds.top).toBeGreaterThanOrEqual(0);
  expect(bounds.bottom).toBeLessThanOrEqual(await page.evaluate(() => window.innerHeight));
}
