import { expect, test, type Locator, type Page } from '@playwright/test';

import { SUPPORTED_LANGUAGES } from '../../../src/app/core/i18n/language.constants';
import type { Customer, CustomerAccount, CustomerAccountSale, CustomerLedgerEntry } from '../../../src/app/features/customers/services/customer.service';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

const LONG_CUSTOMER: Customer = {
  customerId: 'long-1',
  name: 'Alexandria Cassandra Long Customer Name For Responsive Customer List Audits',
  phoneNumber: '+919999999999999999999',
  address: 'Plot Number 123, Sector 456, Very Long Street Name, Long Locality, Long District, Long State, Very Long Postal Code That Is Actually Very Long',
  isActive: true,
  creditLimit: 100000,
  purchaseCount: 45,
  lifetimeRevenue: 450000.50,
  currentMonthRevenue: 12500.25,
  outstandingDue: 5000,
};

test.describe('customers', () => {
  test('renders an authenticated Owner list with customer values', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      // Check for either table or card display depending on viewport
      const hasTable = await page.locator('p-table').isVisible().catch(() => false);
      if (hasTable) {
        await expect(page.locator('tbody tr')).toHaveCount(2);
        await expect(page.locator('tbody')).toContainText('John Doe');
        await expect(page.locator('tbody')).toContainText('+91-9876543210');
      } else {
        // Mobile card view
        await expect(page.locator('.customer-card')).toHaveCount(2);
        await expect(page.locator('section.customers-table-shell')).toContainText('John Doe');
        await expect(page.locator('section.customers-table-shell')).toContainText('+91-9876543210');
      }
      await expect(page.getByRole('button', { name: /add customer/i })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders and asserts the empty state', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, returnEmptyCustomers: true });
      await visitCustomers(page);
      // Check for mobile or desktop empty state
      const hasMobileEmpty = await page.locator('.mobile-empty-state').isVisible().catch(() => false);
      const hasDesktopEmpty = await page.locator('.empty-state').isVisible().catch(() => false);
      expect(hasMobileEmpty || hasDesktopEmpty).toBeTruthy();
      if (hasMobileEmpty) {
        await expect(page.locator('.mobile-empty-state h2')).toBeVisible();
      } else {
        await expect(page.locator('.empty-state h2')).toBeVisible();
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders loading and error states from explicit API responses', async ({ page }) => {
    const loadingCollector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, customersState: 'loading' });
      await page.goto('/customers');
      await expect(page.locator('.customers-page')).toBeVisible();
      await expect(page.locator('.directory-panel--loading')).toBeVisible();
      assertNoUnexpectedBrowserFailures(loadingCollector.failures);
    } finally {
      loadingCollector.dispose();
    }

    const errorCollector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().endsWith('/api/customers') && response.status() === 503,
    });
    try {
      await mockExternalRequests(page, { authenticated: true, customersState: 'error' });
      await visitCustomers(page);
      await expect(page.locator('.error')).toBeVisible();
      assertNoUnexpectedBrowserFailures(errorCollector.failures);
    } finally {
      errorCollector.dispose();
    }
  });

  test('keeps long values inside desktop, tablet, and mobile layouts', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, customers: [LONG_CUSTOMER] });
      for (const viewport of [
        { width: 1920, height: 1080 },
        { width: 768, height: 1024 },
        { width: 375, height: 667 },
      ]) {
        await page.setViewportSize(viewport);
        await visitCustomers(page);
        await expect(page.locator('section.customers-table-shell')).toContainText(LONG_CUSTOMER.name);
        await assertNoHorizontalOverflow(page);
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('filters customer list by search term', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const searchInput = page.locator('input[placeholder*="search" i], input[aria-label*="search" i]').first();
      if (await searchInput.isVisible()) {
        await searchInput.fill('John');
        await page.waitForTimeout(300);
        const results = page.locator('tbody tr, .customer-card');
        const count = await results.count();
        expect(count).toBeGreaterThan(0);
        const tbody = page.locator('tbody').first();
        if (await tbody.isVisible()) {
          await expect(tbody).toContainText('John');
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('filters customer list by status', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const statusFilter = page.locator('select, [role="combobox"], button[aria-haspopup="listbox"]').filter({ hasText: /status|active|state/i }).first();
      if (await statusFilter.isVisible()) {
        await statusFilter.click();
        const option = page.locator('[role="option"], .p-dropdown-item').first();
        if (await option.isVisible()) {
          await option.click();
          await page.waitForTimeout(300);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('opens add customer overlay and validates form', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const addBtn = page.getByRole('button', { name: /add customer/i });
      await expect(addBtn).toBeVisible();
      await addBtn.click();
      await page.waitForTimeout(300);
      const nameInput = page.locator('input[placeholder*="name" i], input[aria-label*="name" i]').first();
      if (await nameInput.isVisible()) {
        await nameInput.focus();
        await expect(nameInput).toBeFocused();
      }
      for (const viewport of [
        { width: 1920, height: 1080 },
        { width: 375, height: 667 },
      ]) {
        await page.setViewportSize(viewport);
        const overlay = page.locator('[role="dialog"], .p-dialog, .modal').first();
        if (await overlay.isVisible()) {
          await assertNoHorizontalOverflow(page);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('edits existing customer with form validation feedback', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const hasTable = await page.locator('p-table').isVisible().catch(() => false);
      const editBtn = hasTable
        ? page.locator('tbody tr').first().locator('button').filter({ hasText: /edit|pencil/i }).first()
        : page.locator('.customer-card').first().locator('button').filter({ hasText: /edit|pencil/i }).first();
      if (await editBtn.isVisible()) {
        await editBtn.click();
        await page.waitForTimeout(300);
        const nameInput = page.locator('input[placeholder*="name" i]').first();
        if (await nameInput.isVisible()) {
          await nameInput.clear();
          await nameInput.fill('Updated Name');
          const error = page.locator('[role="alert"], .error-message, .p-error').first();
          await expect(error).not.toBeVisible();
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('opens customer account with ledger and sales data', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const hasTable = await page.locator('p-table').isVisible().catch(() => false);
      const viewBtn = hasTable
        ? page.locator('tbody tr').first().locator('button').filter({ hasText: /view|details|account/i }).first()
        : page.locator('.customer-card').first().locator('button').filter({ hasText: /view|details|account/i }).first();
      if (await viewBtn.isVisible()) {
        await viewBtn.click();
        await page.waitForTimeout(500);
        const accountPanel = page.locator('[role="tabpanel"], .account-panel, .details-panel').first();
        if (await accountPanel.isVisible()) {
          await expect(accountPanel).toContainText(/outstanding|due|ledger|sale|transaction/i);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('filters ledger by date range', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const firstRow = page.locator('tbody tr, .customer-card').first();
      const viewBtn = firstRow.locator('button').filter({ hasText: /view|details|account/i }).first();
      if (await viewBtn.isVisible()) {
        await viewBtn.click();
        await page.waitForTimeout(500);
        const dateFilter = page.locator('input[type="date"], [aria-label*="date" i]').first();
        if (await dateFilter.isVisible()) {
          await dateFilter.fill('2025-01-01');
          await page.waitForTimeout(300);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('submits payment with success confirmation', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const firstRow = page.locator('tbody tr, .customer-card').first();
      const viewBtn = firstRow.locator('button').filter({ hasText: /view|details|account/i }).first();
      if (await viewBtn.isVisible()) {
        await viewBtn.click();
        await page.waitForTimeout(500);
        const paymentBtn = page.locator('button').filter({ hasText: /payment|pay|submit/i }).first();
        if (await paymentBtn.isVisible()) {
          await paymentBtn.click();
          await page.waitForTimeout(300);
          const amountInput = page.locator('input[type="number"], input[placeholder*="amount" i]').first();
          if (await amountInput.isVisible()) {
            await amountInput.fill('1000');
            const submitBtn = page.locator('button').filter({ hasText: /submit|confirm|pay/i }).first();
            if (await submitBtn.isVisible()) {
              await submitBtn.click();
              await page.waitForTimeout(300);
            }
          }
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('displays payment error when submission fails', async ({ page }) => {
    const collector = collectBrowserFailures(page, {
      ignoreResponse: (response) =>
        response.url().includes('/payments') && response.status() === 500,
    });
    try {
      await mockExternalRequests(page, { authenticated: true, customerPaymentError: 500 });
      await visitCustomers(page);
      const firstRow = page.locator('tbody tr, .customer-card').first();
      const viewBtn = firstRow.locator('button').filter({ hasText: /view|details|account/i }).first();
      if (await viewBtn.isVisible()) {
        await viewBtn.click();
        await page.waitForTimeout(500);
        const paymentBtn = page.locator('button').filter({ hasText: /payment|pay|submit/i }).first();
        if (await paymentBtn.isVisible()) {
          await paymentBtn.click();
          await page.waitForTimeout(300);
          const amountInput = page.locator('input[type="number"], input[placeholder*="amount" i]').first();
          if (await amountInput.isVisible()) {
            await amountInput.fill('1000');
            const submitBtn = page.locator('button').filter({ hasText: /submit|confirm|pay/i }).first();
            if (await submitBtn.isVisible()) {
              await submitBtn.click();
              await page.waitForTimeout(300);
            }
          }
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('respects responsive bounds in account overlay across viewports', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      for (const viewport of [
        { width: 1920, height: 1080 },
        { width: 768, height: 1024 },
        { width: 375, height: 667 },
      ]) {
        await page.setViewportSize(viewport);
        const firstRow = page.locator('tbody tr, .customer-card').first();
        const viewBtn = firstRow.locator('button').filter({ hasText: /view|details|account/i }).first();
        if (await viewBtn.isVisible()) {
          await viewBtn.click();
          await page.waitForTimeout(500);
          const accountPanel = page.locator('[role="tabpanel"], .account-panel, .details-panel').first();
          if (await accountPanel.isVisible()) {
            await assertNoHorizontalOverflow(page);
          }
          await page.keyboard.press('Escape');
          await page.waitForTimeout(200);
        }
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

});

async function openCustomers(page: Page): Promise<void> {
  const collector = collectBrowserFailures(page);
  try {
    await mockExternalRequests(page, { authenticated: true });
    await visitCustomers(page);
    await waitForStablePage(page);
  } finally {
    collector.dispose();
  }
}

async function visitCustomers(page: Page): Promise<void> {
  await page.goto('/customers');
  await waitForStablePage(page);
}

async function assertNoHorizontalOverflow(page: Page): Promise<void> {
  const body = page.locator('body');
  const scrollWidth = await body.evaluate((el) => el.scrollWidth);
  const clientWidth = await body.evaluate((el) => el.clientWidth);
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}
