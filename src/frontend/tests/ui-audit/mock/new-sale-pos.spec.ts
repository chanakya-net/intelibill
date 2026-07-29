import { expect, test } from '@playwright/test';

import { assertNoUnexpectedBrowserFailures, collectBrowserFailures } from '../support/audit-page';
import {
  SELLABLES,
  addBatchAt,
  addOfflineBatch,
  assertFitsViewport,
  assertNoHorizontalOverflow,
  installPosScenario,
  openBatchPicker,
  openPos,
} from './new-sale-pos.fixture';
import { seedOfflinePosSnapshot } from './new-sale-pos.offline-fixture';

test.describe('new-sale-pos', () => {
  test('keeps empty-cart search, quick tiles, and batch actions reachable', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await openPos(page);

      await expect(page.locator('.empty-cart')).toBeVisible();
      await expect(page.locator('app-batch-search-bar input')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
      await expect(page.locator('app-batch-search-bar .batch-search-actions button')).toHaveCount(
        3,
      );
      await expect(page.getByRole('button', { name: 'Open Batch Picker' })).toBeVisible();

      await openBatchPicker(page);
      const dialog = page.locator('.p-dialog:visible');
      await expect(dialog.locator('.batch-option')).toHaveCount(SELLABLES.length);
      await assertFitsViewport(dialog);
      const duplicateTile = page
        .locator('.quick-product-tile')
        .filter({ hasText: 'Reusable audit product' });
      await expect(duplicateTile).toHaveCount(2);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps long products, picker choices, cart controls, and checkout reachable', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await openPos(page);

      await openBatchPicker(page);
      const dialog = page.locator('.p-dialog:visible');
      await expect(dialog).toBeVisible();
      await expect(dialog.locator('.batch-option')).toHaveCount(SELLABLES.length);
      await expect(dialog).toContainText(SELLABLES[0].itemName);
      await assertFitsViewport(dialog);

      await addBatchAt(page, 0);
      await addBatchAt(page, 1);
      await addBatchAt(page, 2);
      await addBatchAt(page, 3);
      await expect(page.locator('.cart-table')).toContainText(SELLABLES[0].itemName);
      await expect(page.locator('.cart-item-name')).toHaveCount(4);
      await expect(page.locator('.summary-box')).toContainText('Total Due');

      await page.locator('.advanced-toggle button').first().click();
      await expect(page.locator('.advanced-line-edit').first()).toBeVisible();
      await assertNoHorizontalOverflow(page);
      await page.getByRole('button', { name: 'Record Sale' }).scrollIntoViewIfNeeded();
      await expect(page.getByRole('button', { name: 'Record Sale' })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('renders feedback, confirmation, and action controls without viewport overflow', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await openPos(page);

      await addBatchAt(page, 3, 3);
      await expect(page.locator('.qty-value')).toHaveText('3');
      await expect(page.locator('.cart-table')).toBeVisible();

      const customerName = page.locator('app-sale-customer-section input').first();
      await customerName.fill('Alexandria');
      await page.getByText('Alexandria Cassandra Long Customer Name', { exact: true }).click();
      await expect(page.locator('app-sale-payment-section .p-inputnumber-input')).toHaveCount(2);
      await page.locator('app-sale-payment-section .p-select').click();
      await page.getByRole('option', { name: 'UPI' }).click();
      const payment = page.locator('app-sale-payment-section');
      const paymentInputs = payment.locator('.p-inputnumber-input');
      await paymentInputs.first().fill('100');
      await paymentInputs.first().press('Tab');
      await expect(payment.locator('.p-select-label')).toHaveText('UPI');
      await expect(paymentInputs.first()).toHaveValue('100');
      await expect(paymentInputs.nth(1)).toHaveValue('237.5');

      const summary = page.locator('.summary-box');
      await expect(summary).toContainText('Subtotal₹286.02');
      await expect(summary).toContainText('Tax₹51.48');
      await expect(summary).toContainText('Discount-₹37.50');
      await expect(summary).toContainText('Total Due₹337.50');
      await expect(summary).toContainText('Balance Due₹237.50');

      await page.getByRole('button', { name: 'Record Sale' }).click();
      const confirmation = page.locator('.p-dialog:visible');
      await expect(confirmation).toContainText('Sale Recorded');
      await expect(confirmation.getByRole('button', { name: 'Print A4' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Print Thermal' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Done' })).toBeVisible();
      await assertFitsViewport(confirmation);

      await assertNoHorizontalOverflow(page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps a product-search error visible and checkout actions reachable on mobile', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/sales/sellables') && response.status() === 503,
    });
    try {
      await installPosScenario(page, { sellables: 'error' });
      await page.setViewportSize({ width: 360, height: 800 });
      await openPos(page);

      await page.locator('app-batch-search-bar input').fill('unavailable');
      await page.getByRole('button', { name: 'Search' }).click();
      await expect(page.locator('.new-sale-product-lookup .error')).toBeVisible();
      await expect(page.locator('.empty-cart')).toBeVisible();
      await page.getByRole('button', { name: 'Record Sale' }).scrollIntoViewIfNeeded();
      await expect(page.getByRole('button', { name: 'Record Sale' })).toBeDisabled();
      await assertNoHorizontalOverflow(page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps dense carts and validation feedback usable on narrow screens', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await page.setViewportSize({ width: 360, height: 800 });
      await openPos(page);

      await openBatchPicker(page);
      for (let index = 0; index < 8; index++) {
        await addBatchAt(page, index);
      }
      await expect(page.locator('.cart-item-name')).toHaveCount(8);
      await page.locator('.qty-controls').first().getByRole('button').nth(1).click();
      await expect(page.locator('.qty-value').first()).toHaveText('2');
      await page.locator('app-sale-customer-section input[type="tel"]').fill('bad-phone');
      await page.locator('app-sale-customer-section input[type="tel"]').blur();
      await expect(page.locator('app-sale-customer-section .error-hint')).toBeVisible();
      await page.getByRole('button', { name: 'Record Sale' }).scrollIntoViewIfNeeded();
      await assertNoHorizontalOverflow(page);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('uses IndexedDB-backed offline catalog, banner, and queued confirmation', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/ping') && response.status() === 503,
    });
    try {
      await installPosScenario(page, { offline: true });
      await seedOfflinePosSnapshot(page);
      await openPos(page);

      await expect(page.locator('.status-chip')).toBeVisible();
      await expect(page.locator('.offline-status-banner')).toBeVisible();
      await expect(page.locator('app-sale-customer-section')).toContainText('cached');
      await page.locator('app-batch-search-bar input').fill('offline');
      await page.getByRole('button', { name: 'Search' }).click();
      await expect(page.locator('.p-dialog:visible .batch-option')).toHaveCount(1);
      await addOfflineBatch(page);
      await page.getByRole('button', { name: 'Record Sale' }).click();
      const confirmation = page.locator('.p-dialog:visible');
      await expect(confirmation).toContainText('Sale Queued for Sync');
      await expect(confirmation.getByRole('button', { name: 'Print A4' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Print Thermal' })).toBeVisible();
      await expect(confirmation.getByRole('button', { name: 'Done' })).toBeVisible();
      await assertFitsViewport(confirmation);
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});
