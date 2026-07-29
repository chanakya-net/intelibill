import {
  expect,
  test,
  type Page,
} from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  waitForStablePage,
} from '../support/audit-page';
import {
  DENSE_PURCHASE_ORDERS,
  LONG_PURCHASE_ORDER,
  PURCHASE_ORDER_STATUSES,
  createPurchaseOrdersScenario,
  installPurchaseOrdersFixture,
  type PurchaseOrdersScenario,
} from '../fixtures/purchase-orders.fixture';

test.describe('purchase-order-detail', () => {
  test('renders draft status with edit, place order, delete buttons', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [PURCHASE_ORDER_STATUSES[0]!], // Draft
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(`/inventory/purchase-orders/${PURCHASE_ORDER_STATUSES[0]!.purchaseOrderId}`);
      await waitForStablePage(page);

      // PO detail page header
      await expect(page.locator('.po-hero')).toBeVisible();

      // Draft-specific action buttons
      await expect(page.getByRole('button', { name: /edit/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /place order/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /delete/i })).toBeVisible();

      // Status pill shows Draft
      await expect(page.locator('.po-status-pill')).toContainText(/Draft/i);

      // Summary cards present
      await expect(page.locator('.summary-card')).toHaveCount(4);

      // Lines table present
      await expect(page.locator('p-table')).toBeVisible();

      // Receipt history component rendered (may be hidden by CSS if no receipts)
      await expect(page.locator('app-purchase-order-receipt-history')).not.toHaveCount(0);

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders placed status with receive button, no edit', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [PURCHASE_ORDER_STATUSES[1]!], // Placed
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(`/inventory/purchase-orders/${PURCHASE_ORDER_STATUSES[1]!.purchaseOrderId}`);
      await waitForStablePage(page);

      // Placed status visible
      await expect(page.locator('.po-status-pill')).toContainText(/Placed/i);

      // Receive button visible
      await expect(page.getByRole('button', { name: /receive/i })).toBeVisible();

      // Edit button hidden
      await expect(page.getByRole('button', { name: /edit/i })).not.toBeVisible();

      // Summary cards & table visible
      await expect(page.locator('.summary-card')).toHaveCount(4);
      await expect(page.locator('p-table')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders partially received status with receive & close buttons', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [PURCHASE_ORDER_STATUSES[2]!], // PartiallyReceived
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(`/inventory/purchase-orders/${PURCHASE_ORDER_STATUSES[2]!.purchaseOrderId}`);
      await waitForStablePage(page);

      // Status visible (localized text)
      await expect(page.locator('.po-status-pill')).toContainText(/Partially/i);

      // Receive button visible
      await expect(page.getByRole('button', { name: /receive/i })).toBeVisible();

      // Summary & table visible
      await expect(page.locator('.summary-card')).toHaveCount(4);
      await expect(page.locator('p-table')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders received status (read-only)', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [PURCHASE_ORDER_STATUSES[3]!], // Received
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(`/inventory/purchase-orders/${PURCHASE_ORDER_STATUSES[3]!.purchaseOrderId}`);
      await waitForStablePage(page);

      // Status visible
      await expect(page.locator('.po-status-pill')).toContainText(/Received/i);

      // No action buttons
      await expect(page.getByRole('button', { name: /receive/i })).not.toBeVisible();
      await expect(page.getByRole('button', { name: /edit/i })).not.toBeVisible();

      // Summary & table visible (read-only view)
      await expect(page.locator('.summary-card')).toHaveCount(4);
      await expect(page.locator('p-table')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });


  test('renders closed status (read-only)', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [PURCHASE_ORDER_STATUSES[4]!], // Closed
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(`/inventory/purchase-orders/${PURCHASE_ORDER_STATUSES[4]!.purchaseOrderId}`);
      await waitForStablePage(page);

      await expect(page.locator('.po-status-pill')).toContainText(/Closed/i);
      await expect(page.locator('.summary-card')).toHaveCount(4);
      await expect(page.locator('p-table')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders cancelled status (read-only)', async ({
    page,
  }) => {
    const scenario = createPurchaseOrdersScenario({
      orders: [PURCHASE_ORDER_STATUSES[5]!], // Cancelled
    });
    const collector = collectBrowserFailures(page);

    try {
      await installPurchaseOrdersFixture(page, scenario);
      await page.goto(`/inventory/purchase-orders/${PURCHASE_ORDER_STATUSES[5]!.purchaseOrderId}`);
      await waitForStablePage(page);

      await expect(page.locator('.po-status-pill')).toContainText(/Cancelled/i);
      await expect(page.locator('.summary-card')).toHaveCount(4);
      await expect(page.locator('p-table')).toBeVisible();

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});
