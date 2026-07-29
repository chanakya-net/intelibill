import { expect, test, type Locator, type Page } from '@playwright/test';

import type { Supplier } from '../../../src/app/features/suppliers/services/supplier.service';
import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  mockExternalRequests,
  waitForStablePage,
} from '../support/audit-page';

const LONG_SUPPLIER: Supplier = {
  supplierId: '1',
  name: 'Alexandria Cassandra Long Supplier Name For Responsive Supplier List Audits',
  contactPersonName: 'Extremely Long Contact Person Name For Supplier',
  contactPersonPhone: '+919999999999999999999',
  address: 'Plot Number 123, Sector 456, Very Long Street Name, Long Locality, Long District, Long State, Very Long Postal Code',
  city: 'Long City Name',
  state: 'Long State Name',
  pin: '560001',
  gstNumber: '29ABCDE1234F1Z5',
  isSystem: false,
  isActive: true,
  isPreferred: true,
  balanceDue: 100000,
};

const FILTER_SUPPLIERS: readonly Supplier[] = [
  {
    supplierId: 'active-supplier',
    name: 'Active Audit Supplier',
    contactPersonName: 'Active Contact',
    contactPersonPhone: '+919876543210',
    address: 'Active Street',
    city: 'Active City',
    state: 'Active State',
    pin: '560001',
    gstNumber: '29ACTIVE1234F1Z5',
    isSystem: false,
    isActive: true,
    isPreferred: false,
    balanceDue: 50000,
  },
  {
    supplierId: 'inactive-supplier',
    name: 'Inactive Audit Supplier',
    contactPersonName: 'Inactive Contact',
    contactPersonPhone: '+919123456789',
    address: 'Inactive Street',
    city: 'Inactive City',
    state: 'Inactive State',
    pin: '560002',
    gstNumber: '29INACTV1234F1Z5',
    isSystem: false,
    isActive: false,
    isPreferred: true,
    balanceDue: 75000,
  },
];

test.describe('suppliers', () => {
  test('renders supplier tables or cards and filters known fixtures', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openSuppliers(page, { suppliers: FILTER_SUPPLIERS });
      const rows = visibleSupplierRows(page);
      await expect(rows).toHaveCount(2);
      await expect(rows).toContainText(['Active Audit Supplier', 'Inactive Audit Supplier']);

      await expect(page.locator('app-suppliers-filter-bar')).toBeVisible();
      const searchInput = page.locator('input[placeholder*="search" i]');
      await expect(searchInput).toBeVisible();
      await searchInput.fill('inactive audit');
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Inactive Audit Supplier');
      await searchInput.fill('');
      await expect(rows).toHaveCount(2);

      await page.locator('p-select[inputid="suppliers-status-filter"]').click();
      await page.getByRole('option', { name: 'suppliers.inactive', exact: true }).click();
      await expect(rows).toHaveCount(1);
      await expect(rows).toContainText('Inactive Audit Supplier');
      await expect(rows).not.toContainText('Active Audit Supplier');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders loading, error, and empty supplier states', async ({ page }) => {
    await openSuppliers(page, { suppliersState: 'loading' });
    await expect(page.locator('.directory-panel--loading[aria-busy="true"]')).toBeVisible();

    const collector = collectBrowserFailures(page, supplierErrorIgnores('/api/suppliers', 503));
    try {
      await openSuppliers(page, { suppliersState: 'error' });
      await expect(page.locator('.error')).toContainText('Unable to load suppliers');
      await expect(page.locator('.directory-panel__surface')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }

    await openSuppliers(page, { returnEmptySuppliers: true });
    const emptyState = page.locator(
      '.desktop-table:visible .empty-state, .mobile-grid-container:visible .mobile-empty-state',
    );
    await expect(emptyState).toBeVisible();
    await expect(emptyState.getByRole('heading')).toHaveText('No suppliers found');
  });

  test('validates, submits, and edits supplier forms', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    let mutationRequests = 0;
    let editPayload: unknown;
    page.on('request', (request) => {
      if (!request.url().includes('/api/suppliers') || request.method() === 'GET') return;
      mutationRequests += 1;
      if (request.method() === 'PUT') editPayload = request.postDataJSON();
    });

    try {
      await openSuppliers(page);
      await page.getByRole('button', { name: /add supplier/i }).click();
      const addOverlay = page.locator('app-add-supplier-overlay .overlay');
      await expect(addOverlay).toBeVisible();
      await addOverlay.getByRole('button', { name: /add supplier/i }).click();
      await expect(addOverlay.locator('input[formcontrolname="name"]')).toHaveClass(/ng-invalid/);
      await expect(addOverlay.locator('input[formcontrolname="contactPersonPhone"]')).toHaveClass(
        /ng-invalid/,
      );
      expect(mutationRequests).toBe(0);

      await fillSupplierForm(addOverlay, {
        name: 'Created Audit Supplier',
        contactPersonName: 'New Contact',
        contactPersonPhone: '+919111111111',
        address: 'Created Street',
        city: 'Created City',
        state: 'Created State',
        pin: '560003',
        gstNumber: '29CREATED1234F1Z5',
      });
      await addOverlay.getByRole('button', { name: /add supplier/i }).click();
      await expect(addOverlay).toBeHidden();
      await expect(
        visibleSupplierRows(page).filter({ hasText: 'Created Audit Supplier' }),
      ).toBeVisible();
      expect(mutationRequests).toBe(1);

      const supplierRow = visibleSupplierRows(page).filter({ hasText: 'Audit Supplier One' });
      await supplierRow.getByRole('button', { name: /edit supplier/i }).click();
      const editOverlay = page.locator('app-edit-supplier-overlay .overlay');
      await expect(editOverlay).toBeVisible();
      await expect(editOverlay.locator('input[formcontrolname="name"]')).toHaveValue('Audit Supplier One');
      await editOverlay.locator('input[formcontrolname="name"]').fill('Updated Audit Supplier');
      await editOverlay.locator('input[formcontrolname="contactPersonPhone"]').fill('+919876543210');
      await editOverlay.getByRole('button', { name: /edit supplier/i }).click();
      expect(mutationRequests).toBe(2);
      expect(editPayload).toMatchObject({ name: 'Updated Audit Supplier' });
      await expect(editOverlay).toBeHidden();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('opens supplier detail and filters ledger goods received and payment rows', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openSuppliers(page);
      const detail = await openSupplierDetail(page);
      await expect(detail).toContainText('Audit Supplier One');
      await expect(detail).toContainText('Balance Due');

      const ledgerRows = detail.locator('.ledger-table tbody tr');
      await expect(ledgerRows).toHaveCount(2);
      await detail.getByRole('button', { name: 'Goods Received', exact: true }).click();
      await expect(ledgerRows).toHaveCount(1);
      await detail.getByRole('button', { name: 'Payments', exact: true }).click();
      await expect(ledgerRows).toHaveCount(1);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('submits a payment and displays the reloaded supplier state', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    let paymentRequests = 0;
    page.on('request', (request) => {
      if (request.method() === 'POST' && request.url().includes('/api/suppliers/') && request.url().includes('/payments')) {
        paymentRequests += 1;
      }
    });
    try {
      await openSuppliers(page);
      const detail = await openSupplierDetail(page);
      await detail.getByRole('spinbutton', { name: 'Amount ₹' }).fill('1000');
      await detail.getByRole('button', { name: /submit payment/i }).click();
      expect(paymentRequests).toBe(1);
      await expect(detail.locator('.ledger-table tbody tr')).toHaveCount(3);
      await detail.getByRole('button', { name: 'Payments', exact: true }).click();
      await expect(detail.locator('.ledger-table tbody tr')).toHaveCount(2);
      await expect(detail).toContainText('₹1,000.00');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('displays supplier detail and payment errors from explicit API responses', async ({ page }) => {
    const detailCollector = collectBrowserFailures(page, supplierErrorIgnores('/detail', 503));
    try {
      await openSuppliers(page, { supplierDetailError: 503 });
      const dialog = await openSupplierDetail(page);
      await expect(dialog.locator('.error')).toHaveText(
        'Unable to load supplier details right now.',
      );
      await expect(dialog.locator('.supplier-detail')).toBeHidden();
      assertNoUnexpectedBrowserFailures(detailCollector.failures);
    } finally {
      detailCollector.dispose();
    }

    const paymentCollector = collectBrowserFailures(page, supplierErrorIgnores('/payments', 500));
    let paymentRequests = 0;
    page.on('request', (request) => {
      if (request.method() === 'POST' && request.url().includes('/api/suppliers/') && request.url().includes('/payments')) {
        paymentRequests += 1;
      }
    });
    try {
      await openSuppliers(page, { supplierPaymentError: 500 });
      const detail = await openSupplierDetail(page);
      await detail.getByRole('spinbutton', { name: 'Amount ₹' }).fill('1000');
      await detail.getByRole('button', { name: /submit payment/i }).click();
      expect(paymentRequests).toBe(1);
      await expect(detail.locator('.error')).toHaveText('Unable to record payment right now.');
      await expect(detail.locator('.ledger-table tbody tr')).toHaveCount(2);
      assertNoUnexpectedBrowserFailures(paymentCollector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps dense long values and overlays within responsive bounds', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await mockExternalRequests(page, { authenticated: true, suppliers: [LONG_SUPPLIER] });
      for (const viewport of [
        { width: 1920, height: 1080 },
        { width: 768, height: 1024 },
        { width: 375, height: 667 },
      ]) {
        await page.setViewportSize(viewport);
        await visitSuppliers(page);
        await expect(visibleSupplierRows(page)).toContainText(LONG_SUPPLIER.name);
        await assertNoHorizontalOverflow(page);

        await page.getByRole('button', { name: /add supplier/i }).click();
        const addOverlay = page.locator('app-add-supplier-overlay .overlay');
        await expect(addOverlay).toBeVisible();
        await assertWithinViewport(page, addOverlay.locator('.overlay-card'));
        await addOverlay.getByRole('button', { name: 'Close', exact: true }).click();

        const detail = await openSupplierDetail(page, LONG_SUPPLIER.name);
        await assertWithinViewport(page, detail);
        await assertNoHorizontalOverflow(page);
        await page.keyboard.press('Escape');
        await expect(detail).toBeHidden();
      }
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});

async function openSuppliers(
  page: Page,
  options: Parameters<typeof mockExternalRequests>[1] = {},
): Promise<void> {
  await mockExternalRequests(page, { authenticated: true, ...options });
  await visitSuppliers(page);
}

async function visitSuppliers(page: Page): Promise<void> {
  await page.goto('/suppliers');
  await waitForStablePage(page);
  await expect(page.locator('.suppliers-page')).toBeVisible();
}

function visibleSupplierRows(page: Page): Locator {
  return page.locator(
    '.desktop-table:visible tbody tr, .mobile-grid-container:visible .supplier-card',
  );
}

async function fillSupplierForm(
  overlay: Locator,
  values: {
    name: string;
    contactPersonName: string;
    contactPersonPhone: string;
    address: string;
    city: string;
    state: string;
    pin: string;
    gstNumber: string;
  },
): Promise<void> {
  await overlay.locator('input[formcontrolname="name"]').fill(values.name);
  await overlay.locator('input[formcontrolname="contactPersonName"]').fill(values.contactPersonName);
  await overlay.locator('input[formcontrolname="contactPersonPhone"]').fill(values.contactPersonPhone);
  await overlay.locator('input[formcontrolname="address"]').fill(values.address);
  await overlay.locator('input[formcontrolname="city"]').fill(values.city);
  await overlay.locator('input[formcontrolname="state"]').fill(values.state);
  await overlay.locator('input[formcontrolname="pin"]').fill(values.pin);
  await overlay.locator('input[formcontrolname="gstNumber"]').fill(values.gstNumber);
}

async function openSupplierDetail(page: Page, supplierName = 'Audit Supplier One'): Promise<Locator> {
  const trigger = page
    .locator('.supplier-name-link:visible, .supplier-card__identity:visible')
    .filter({ hasText: supplierName });
  await expect(trigger).toBeVisible();
  await trigger.click();
  const dialog = page.locator('.supplier-detail-dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('.supplier-detail, .error')).toBeVisible();
  return dialog;
}

function supplierErrorIgnores(
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
