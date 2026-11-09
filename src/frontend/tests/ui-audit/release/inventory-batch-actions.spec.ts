import { expect, test, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { session, SHOP_A, startPurchaseReceivingServer } from './purchase-receiving-server';

test.use({ serviceWorkers: 'block' });

const batch = {
  id: 'batch-menu',
  shopId: SHOP_A,
  itemId: 'item-menu',
  itemName: 'Menu test milk',
  barcode: 'MENU-MILK',
  batchNumber: 'MENU-LOT',
  quantity: 10,
  originalQuantity: 10,
  costPrice: 40,
  salesPrice: 50,
  mrp: 60,
  taxRatePercent: 0,
  taxIncluded: false,
  expiryDate: null,
  manufacturingDate: null,
  supplierId: 'supplier-menu',
  supplierName: 'Menu supplier',
  isVoided: false,
  createdAt: '2026-11-09T08:00:00Z',
  updatedAt: null,
  version: 8,
  quantityScale: 0,
  uom: 'pcs',
  receiptValueId: 'receipt-menu',
  receiptStatus: 'Resolved',
  receiptOriginalTotalCost: 400,
  receiptTotalCost: 400,
  receiptVersion: 3,
};
const targetBatch = {
  ...batch,
  id: 'batch-target',
  itemName: 'Target yoghurt',
  batchNumber: 'TARGET-LOT',
  receiptValueId: 'receipt-target',
};
const json = (route: Route, value: unknown) =>
  route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(value),
  });

for (const width of [1440, 390]) {
  test(`batch menu pointer actions at ${width}px`, async ({ page }) => {
    const server = await startPurchaseReceivingServer(
      resolve(__dirname, '../../../dist/INVENTORY/browser'),
    );
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(
      (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
      session(),
    );
    const voids: string[] = [];
    await page.route('**/api/inventory/batches', (route) => json(route, [batch, targetBatch]));
    await page.route('**/api/inventory/batches/batch-target/void', (route) => {
      voids.push(route.request().method());
      return route.fulfill({ status: 204 });
    });
    try {
      for (const label of [
        'Adjust Batch',
        'Count stock',
        'Edit prices & details',
        'Correct receipt cost',
        'Print labels',
        'Void',
      ]) {
        await page.goto(`${server.origin}/inventory/batches`);
        await page
          .getByRole('button', { name: 'Actions', exact: true })
          .filter({ visible: true })
          .last()
          .click();
        const item = page.locator('.p-menu:visible').getByText(label, { exact: true });
        await expect(item).toBeVisible();
        // Exercise pointer focus/blur between down and up, like a physical click.
        await item.click({ delay: 80 });
        if (label === 'Void') await expect.poll(() => voids).toEqual(['POST']);
        else {
          const dialog = page.getByRole('dialog');
          await expect(dialog).toBeVisible();
          await expect(dialog).toContainText(
            label === 'Print labels' ? targetBatch.itemName : targetBatch.batchNumber,
          );
          if (label !== 'Print labels') await expect(dialog).toHaveAccessibleName(label);
        }
      }
      await page.goto(`${server.origin}/inventory/batches`);
      const actions = page
        .getByRole('button', { name: 'Actions', exact: true })
        .filter({ visible: true });
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await actions.last().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await page.getByRole('menuitem', { name: 'Adjust Batch', exact: true }).click({ delay: 80 });
      await expect(page.getByRole('dialog', { name: 'Adjust Batch', exact: true })).toContainText(
        targetBatch.batchNumber,
      );
      await page.goto(`${server.origin}/inventory/batches`);
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeVisible();
      await actions.first().click();
      await expect(page.getByRole('menu')).toBeHidden();
      // Reopening for another row refreshes the shared popup's target.
      await actions.first().focus();
      await page.keyboard.press('Enter');
      const menu = page.getByRole('menu');
      await expect(menu).toBeFocused();
      await page.keyboard.press('Home');
      await expect(menu).toHaveAttribute('aria-activedescendant', 'printLabels');
      await page.keyboard.press('ArrowDown');
      await expect(menu).toHaveAttribute('aria-activedescendant', 'adjust');
      await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog', { name: 'Adjust Batch', exact: true })).toContainText(
        batch.batchNumber,
      );
    } finally {
      await server.close();
    }
  });
}
