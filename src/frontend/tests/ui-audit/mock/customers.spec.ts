import { expect, test, type Locator, type Page } from '@playwright/test';

import type { Customer } from '../../../src/app/features/customers/services/customer.service';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

const LONG_CUSTOMER: Customer = {
  customerId: '1',
  name: 'Alexandria Cassandra Long Customer Name For Responsive Customer List Audits',
  phoneNumber: '+919999999999999999999',
  address:
    'Plot Number 123, Sector 456, Very Long Street Name, Long Locality, Long District, Long State, Very Long Postal Code That Is Actually Very Long',
  isActive: true,
  creditLimit: 100000,
  purchaseCount: 45,
  lifetimeRevenue: 450000.5,
  currentMonthRevenue: 12500.25,
  outstandingDue: 5000,
};

const FILTER_CUSTOMERS: readonly Customer[] = [
  {
    customerId: 'active-customer',
    name: 'Active Audit Customer',
    phoneNumber: '+919876543210',
    address: 'Active Street',
    isActive: true,
    creditLimit: 50000,
    purchaseCount: 10,
    lifetimeRevenue: 150000,
    currentMonthRevenue: 5000,
    outstandingDue: 2000,
  },
  {
    customerId: 'inactive-customer',
    name: 'Inactive Audit Customer',
    phoneNumber: '+919123456789',
    address: 'Inactive Street',
    isActive: false,
    creditLimit: 75000,
    purchaseCount: 25,
    lifetimeRevenue: 350000,
    currentMonthRevenue: 12000,
    outstandingDue: 0,
  },
];

test.describe('customers', () => {
  test('renders customer tables or cards and filters known fixtures', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page, { customers: FILTER_CUSTOMERS });
      const rows = visibleCustomerRows(page);
      await expect(rows).toHaveCount(2);
      await expect(rows).toContainText(['Active Audit Customer', 'Inactive Audit Customer']);

      await page.locator('input[placeholder*="search" i]').fill('inactive audit');
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Inactive Audit Customer');
      await page.locator('input[placeholder*="search" i]').fill('');
      await expect(rows).toHaveCount(2);

      await page.locator('p-select[inputid="customers-status-filter"]').click();
      await page.getByRole('option', { name: 'customers.inactive', exact: true }).click();
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Inactive Audit Customer');
      await expect(rows).not.toContainText('Active Audit Customer');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders loading, error, and empty customer states', async ({ page }) => {
    await openCustomers(page, { customersState: 'loading' });
    await expect(page.locator('.directory-panel--loading[aria-busy="true"]')).toBeVisible();

    const collector = collectBrowserFailures(page, customerErrorIgnores('/api/customers', 503));
    try {
      await openCustomers(page, { customersState: 'error' });
      await expect(page.locator('.error')).toHaveText('Unable to load customers.');
      await expect(page.locator('.directory-panel__surface')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }

    await openCustomers(page, { returnEmptyCustomers: true });
    const emptyState = page.locator(
      '.desktop-table:visible .empty-state, .mobile-grid-container:visible .mobile-empty-state',
    );
    await expect(emptyState).toBeVisible();
    await expect(emptyState.getByRole('heading')).toHaveText('No customers found');
  });

  test('validates, submits, and edits customer forms', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    let mutationRequests = 0;
    let editPayload: unknown;
    page.on('request', (request) => {
      if (!request.url().includes('/api/customers') || request.method() === 'GET') return;
      mutationRequests += 1;
      if (request.method() === 'PUT') editPayload = request.postDataJSON();
    });

    try {
      await openCustomers(page);
      await page.getByRole('button', { name: /add customer/i }).click();
      const addOverlay = page.locator('app-add-customer-overlay .overlay');
      await expect(addOverlay).toBeVisible();
      await addOverlay.getByRole('button', { name: /add customer/i }).click();
      await expect(addOverlay.locator('input[formcontrolname="name"]')).toHaveClass(/ng-invalid/);
      await expect(addOverlay.locator('input[formcontrolname="phoneNumber"]')).toHaveClass(
        /ng-invalid/,
      );
      expect(mutationRequests).toBe(0);

      await fillCustomerForm(addOverlay, {
        name: 'Created Audit Customer',
        phoneNumber: '+919111111111',
        address: 'Created Street',
        creditLimit: '1200',
      });
      await addOverlay.getByRole('button', { name: /add customer/i }).click();
      await expect(addOverlay).toBeHidden();
      await expect(
        visibleCustomerRows(page).filter({ hasText: 'Created Audit Customer' }),
      ).toBeVisible();
      expect(mutationRequests).toBe(1);

      const customerRow = visibleCustomerRows(page).filter({ hasText: 'John Doe' });
      await customerRow.getByRole('button', { name: /edit customer/i }).click();
      const editOverlay = page.locator('app-edit-customer-overlay .overlay');
      await expect(editOverlay).toBeVisible();
      await expect(editOverlay.locator('input[formcontrolname="name"]')).toHaveValue('John Doe');
      await editOverlay.locator('input[formcontrolname="name"]').fill('Updated Audit Customer');
      await editOverlay.locator('input[formcontrolname="phoneNumber"]').fill('+919876543210');
      await editOverlay.getByRole('button', { name: /edit customer/i }).click();
      expect(mutationRequests).toBe(2);
      expect(editPayload).toMatchObject({ name: 'Updated Audit Customer' });
      await expect(editOverlay).toBeHidden();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('opens the customer account and filters ledger purchase and payment rows', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openCustomers(page);
      const account = await openCustomerAccount(page);
      await expect(account).toContainText('John Doe');
      await expect(account).toContainText('Outstanding Due');
      await expect(account).toContainText('INV-001');

      const ledgerRows = account.locator('.statement-table tbody tr');
      await expect(ledgerRows).toHaveCount(2);
      await account.getByRole('button', { name: 'Purchases', exact: true }).click();
      await expect(ledgerRows).toHaveCount(1);
      await expect(ledgerRows).toContainText('Sale Due');
      await account.getByRole('button', { name: 'Payments', exact: true }).click();
      await expect(ledgerRows).toHaveCount(1);
      await expect(ledgerRows).toContainText('Payment Received');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('submits a payment and displays the reloaded account state', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    let paymentRequests = 0;
    page.on('request', (request) => {
      if (request.method() === 'POST' && request.url().includes('/api/customers/1/payments')) {
        paymentRequests += 1;
      }
    });
    try {
      await openCustomers(page);
      const account = await openCustomerAccount(page);
      await account.getByRole('spinbutton', { name: 'Amount ₹' }).fill('1000');
      await account.getByRole('button', { name: /submit payment/i }).click();
      expect(paymentRequests).toBe(1);
      await expect(account.locator('.statement-table tbody tr')).toHaveCount(3);
      await account.getByRole('button', { name: 'Payments', exact: true }).click();
      await expect(account.locator('.statement-table tbody tr')).toHaveCount(2);
      await expect(account).toContainText('₹1,000.00');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('displays account and payment errors from explicit API responses', async ({ page }) => {
    const accountCollector = collectBrowserFailures(page, customerErrorIgnores('/account', 503));
    try {
      await openCustomers(page, { customerAccountError: 503 });
      const dialog = await openCustomerAccount(page);
      await expect(dialog.locator('.error')).toHaveText(
        'Unable to load customer account right now.',
      );
      await expect(dialog.locator('.account-statement')).toBeHidden();
      assertNoUnexpectedBrowserFailures(accountCollector.failures);
    } finally {
      accountCollector.dispose();
    }

    const paymentCollector = collectBrowserFailures(page, customerErrorIgnores('/payments', 500));
    let paymentRequests = 0;
    page.on('request', (request) => {
      if (request.method() === 'POST' && request.url().includes('/api/customers/1/payments')) {
        paymentRequests += 1;
      }
    });
    try {
      await openCustomers(page, { customerPaymentError: 500 });
      const account = await openCustomerAccount(page);
      await account.getByRole('spinbutton', { name: 'Amount ₹' }).fill('1000');
      await account.getByRole('button', { name: /submit payment/i }).click();
      expect(paymentRequests).toBe(1);
      await expect(account.locator('.error')).toHaveText('Unable to record payment right now.');
      await expect(account.locator('.statement-table tbody tr')).toHaveCount(2);
      assertNoUnexpectedBrowserFailures(paymentCollector.failures);
    } finally {
      paymentCollector.dispose();
    }
  });

  test('keeps dense long values and overlays within responsive bounds', async ({ page }) => {
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
        await expect(visibleCustomerRows(page)).toContainText(LONG_CUSTOMER.name);
        await assertNoHorizontalOverflow(page);

        await page.getByRole('button', { name: /add customer/i }).click();
        const addOverlay = page.locator('app-add-customer-overlay .overlay');
        await expect(addOverlay).toBeVisible();
        await assertWithinViewport(page, addOverlay.locator('.overlay-card'));
        await addOverlay.getByRole('button', { name: 'Close', exact: true }).click();

        const account = await openCustomerAccount(page, LONG_CUSTOMER.name);
        await assertWithinViewport(page, account);
        await assertNoHorizontalOverflow(page);
        await page.keyboard.press('Escape');
        await expect(account).toBeHidden();
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function openCustomers(
  page: Page,
  options: Parameters<typeof mockExternalRequests>[1] = {},
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, ...options });
  await visitCustomers(page);
}

async function visitCustomers(page: Page): Promise<void> {
  await page.goto('/customers');
  await waitForStablePage(page);
  await expect(page.locator('.customers-page')).toBeVisible();
}

function visibleCustomerRows(page: Page): Locator {
  return page.locator(
    '.desktop-table:visible tbody tr, .mobile-grid-container:visible .customer-card',
  );
}

async function fillCustomerForm(
  overlay: Locator,
  values: { name: string; phoneNumber: string; address: string; creditLimit: string },
): Promise<void> {
  await overlay.locator('input[formcontrolname="name"]').fill(values.name);
  await overlay.locator('input[formcontrolname="phoneNumber"]').fill(values.phoneNumber);
  await overlay.locator('input[formcontrolname="address"]').fill(values.address);
  await overlay.getByRole('spinbutton', { name: 'Credit Limit ₹' }).fill(values.creditLimit);
}

async function openCustomerAccount(page: Page, customerName = 'John Doe'): Promise<Locator> {
  const trigger = page
    .locator('.customer-name-link:visible, .customer-card__identity:visible')
    .filter({ hasText: customerName });
  await expect(trigger).toBeVisible();
  await trigger.click();
  const dialog = page.locator('.account-statement-dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('.account-statement, .error')).toBeVisible();
  return dialog;
}

function customerErrorIgnores(
  path: string,
  status: number,
): {
  ignoreConsole: (message: string) => boolean;
  ignoreResponse: (response: { url: () => string; status: () => number }) => boolean;
} {
  return {
    ignoreConsole: (message) =>
      message.includes(String(status)) && message.includes('Failed to load resource'),
    ignoreResponse: (response) => response.url().includes(path) && response.status() === status,
  };
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

async function assertWithinViewport(page: Page, locator: Locator): Promise<void> {
  const bounds = await locator.evaluate((element) => {
    const box = element.getBoundingClientRect();
    return { left: box.left, right: box.right, top: box.top, bottom: box.bottom };
  });
  const viewport = await page.evaluate(() => ({
    width: window.innerWidth,
    height: window.innerHeight,
  }));
  expect(bounds.left).toBeGreaterThanOrEqual(0);
  expect(bounds.right).toBeLessThanOrEqual(viewport.width);
  expect(bounds.top).toBeGreaterThanOrEqual(0);
  expect(bounds.bottom).toBeLessThanOrEqual(viewport.height);
}
