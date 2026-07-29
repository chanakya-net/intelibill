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
