import { expect, test } from '@playwright/test';

import { assertNoUnexpectedBrowserFailures, collectBrowserFailures } from '../support/audit-page';
import { SELLABLES, addBatchAt, installPosScenario, openPos } from './new-sale-pos.fixture';

const KG_ITEM = {
  ...SELLABLES[3],
  inventoryBatchId: 'batch-kg',
  itemName: 'Loose rice',
  batchNumber: 'BATCH-KG-001',
  barcode: 'batch-kg-barcode',
  salesPrice: 100,
  uom: 'kg',
  quantityScale: 3,
};

const PCS_ITEM = {
  ...SELLABLES[3],
  inventoryBatchId: 'batch-pcs',
  itemName: 'Steel spoon',
  batchNumber: 'BATCH-PCS-001',
  barcode: 'batch-pcs-barcode',
  salesPrice: 100,
  uom: 'pcs',
  quantityScale: 0,
};

test.describe('new-sale-fractional-quantity', () => {
  test('kg line takes a typed 0.25 and prices it; pcs line refuses 0.5 and keeps 1', async ({
    page,
  }) => {
    const collector = collectBrowserFailures(page);
    try {
      await installPosScenario(page);
      await page.route('**/api/sales/sellables?**', (route) =>
        route.fulfill({
          contentType: 'application/json',
          body: JSON.stringify([KG_ITEM, PCS_ITEM]),
        }),
      );
      await openPos(page);

      await page.locator('app-batch-search-bar input').fill('audit');
      await page.getByRole('button', { name: 'Search' }).click();
      await addBatchAt(page, 0);
      await addBatchAt(page, 1);
      await expect(page.locator('.cart-item-name')).toHaveCount(2);

      const kgInput = page.getByLabel('Quantity of Loose rice').first();
      const preview = page.waitForResponse(
        (response) => new URL(response.url()).pathname === '/api/sales/preview',
      );
      await kgInput.fill('0.25');
      await kgInput.press('Tab');
      await preview;
      await expect(kgInput).toHaveValue('0.25');
      await expect(page.locator('.cart-line-total').first()).toContainText('25.00');

      const pcsInput = page.getByLabel('Quantity of Steel spoon').first();
      await pcsInput.fill('0.5');
      await pcsInput.press('Tab');
      await expect(page.locator('.quantity-field-error')).toContainText(
        'Too many decimals for pcs: whole numbers only.',
      );
      await expect(pcsInput).toHaveAttribute('aria-invalid', 'true');
      await expect(page.locator('.cart-line-total').nth(1)).toContainText('100.00');
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });
});
