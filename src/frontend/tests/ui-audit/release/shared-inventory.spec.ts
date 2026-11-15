import { expect, test } from '@playwright/test';
import { resolve } from 'node:path';
import { INVENTORY_ITEMS } from '../fixtures/inventory.fixture';
import { session, startPurchaseReceivingServer } from './purchase-receiving-server';

const merchant = resolve('dist/INVENTORY/browser');

test('merchant Owner keeps the existing editor error, form and barcode confirmation', async ({
  page,
}) => {
  const server = await startPurchaseReceivingServer(merchant);
  try {
    await page.addInitScript((value) => {
      localStorage.setItem('inventory.auth.session.local', JSON.stringify(value));
    }, session());

    let attempts = 0;
    await page.route(/\/api\/items(?:\?|$)/, async (route) => {
      if (route.request().method() === 'GET') {
        await route.fulfill({
          json: {
            items: [],
            totalCount: 0,
            pageNumber: 1,
            pageSize: 20,
            summary: {
              totalItems: 0,
              activeItems: 0,
              inactiveItems: 0,
              runningLowStockCount: 0,
              criticalStockCount: 0,
              totalStockValue: 0,
            },
          },
        });
        return;
      }
      attempts++;
      if (attempts === 1) {
        await route.fulfill({ status: 409, json: { code: 'Item.BarcodeAlreadyExists' } });
        return;
      }
      await route.fulfill({ json: INVENTORY_ITEMS[0] });
    });

    await page.goto(server.origin + '/inventory');
    await page.getByRole('button', { name: 'Add New Product' }).click();
    const editor = page.locator('app-add-product-overlay');
    await expect(editor.locator('.ib-form-section').first()).toHaveCSS('display', 'grid');
    await expect(editor.locator('.ib-form-section').first()).toHaveCSS(
      'border-top-color',
      'rgb(234, 215, 193)',
    );
    await editor.locator('#add-product-name').fill('Fixture product');
    await editor.locator('#add-product-uom').fill('PCS');
    await editor.locator('app-inventory-barcode-field input').fill('FIXTURE-001');
    const submit = editor.getByRole('button', { name: 'Add New Product' });
    await submit.click();
    await expect(editor.getByRole('alert')).toBeVisible();
    await expect(editor.getByRole('alert')).toHaveCSS('color', 'rgb(185, 28, 28)');
    await submit.click();

    const confirmation = page.getByRole('alertdialog', { name: 'Print barcode?' });
    await expect(confirmation).toBeVisible();
    await confirmation.locator('.p-confirmdialog-reject-button').click();
    await expect(confirmation).toBeHidden();
    await expect(page.locator('app-barcode-label-print-dialog .p-dialog')).toBeHidden();
    expect(attempts).toBe(2);
  } finally {
    await server.close();
  }
});
