import { test, expect, type Page, type Route } from '@playwright/test';
import { resolve } from 'node:path';
import { mkdir } from 'node:fs/promises';
import { session, SHOP_A, USER, startPurchaseReceivingServer } from './purchase-receiving-server';
const OUTPUT = resolve(__dirname, '../../../.ui-audit/expiry-task5');
const json = (r: Route, x: unknown, status = 200) =>
  r.fulfill({ status, contentType: 'application/json', body: JSON.stringify(x) });
const today = () =>
  new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Kolkata',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
const policy = {
  version: 2,
  mode: 'Warn',
  expiryRequired: true,
  sellThroughExpiryDate: true,
  warningDays: 7,
  overrideAllowed: true,
};
const state = {
  defaultPolicy: policy,
  effectivePolicy: policy,
  overridePolicy: null,
  catalogVersion: 3,
};
const decision = (allowed = true, approval = false) => ({
  allowed,
  reasonCode: allowed ? (approval ? 'Expiry.Expired' : null) : 'Expiry.RequiredDateMissing',
  warning: approval,
  requiresApproval: approval,
  policyVersion: 2,
  catalogVersion: 3,
  businessDate: today(),
  physicalQuantity: 10,
  quarantinedQuantity: 3,
  availableQuantity: allowed ? 7 : 0,
});
const batch = {
  id: 'batch-held',
  shopId: SHOP_A,
  itemId: 'item-one',
  itemName: 'Milk',
  barcode: 'MILK',
  batchNumber: 'RETURN-LOT',
  quantity: 10,
  originalQuantity: 10,
  quarantinedQuantity: 3,
  availableQuantity: 7,
  costPrice: 40,
  salesPrice: 50,
  mrp: 60,
  taxRatePercent: 0,
  taxIncluded: false,
  purchaseTaxIncluded: false,
  expiryDate: '2026-01-01',
  manufacturingDate: '2025-01-01',
  supplierId: null,
  supplierName: null,
  isVoided: false,
  createdAt: new Date().toISOString(),
  updatedAt: null,
  version: 8,
  quantityScale: 3,
  uom: 'kg',
  sellability: decision(true, true),
};
const history = [
  {
    id: 'root-held',
    inventoryBatchId: batch.id,
    rootChangeId: null,
    action: 'Quarantine',
    quantity: 3,
    sourceKind: 'SaleReturn',
    sourceId: 'sale-return-one',
    sourceLineId: 'line-one',
    expiryDate: '2026-01-01',
    manufacturingDate: '2025-01-01',
    reasonCode: 'Expiry.Expired',
    reason: 'Expiry.Expired',
    businessDate: today(),
    recordedAt: new Date().toISOString(),
    actorUserId: USER,
    policyJson: JSON.stringify(policy),
  },
];
const sellables = [
  {
    kind: 'Goods',
    inventoryBatchId: 'blocked-lot',
    barcode: 'MILK',
    itemName: 'Milk',
    batchNumber: 'NO-DATE',
    quantity: 10,
    availableQuantity: 0,
    quarantinedQuantity: 3,
    salesPrice: 50,
    mrp: 60,
    taxRatePercent: 0,
    taxIncluded: false,
    expiryDate: null,
    version: 8,
    quantityScale: 3,
    uom: 'kg',
    sellability: decision(false),
  },
  {
    kind: 'Goods',
    inventoryBatchId: batch.id,
    barcode: 'MILK',
    itemName: 'Milk',
    batchNumber: 'EXPIRED-WARN',
    quantity: 10,
    availableQuantity: 7,
    quarantinedQuantity: 3,
    salesPrice: 50,
    mrp: 60,
    taxRatePercent: 0,
    taxIncluded: false,
    expiryDate: '2026-01-01',
    version: 8,
    quantityScale: 3,
    uom: 'kg',
    sellability: decision(true, true),
  },
];
const sale = {
  saleId: 'sale-one',
  invoiceNumber: 'A/2627/000123',
  customerId: null,
  customerName: 'Walk-in',
  customerPhone: null,
  paymentMethod: 1,
  soldAt: new Date().toISOString(),
  paidAmount: 100,
  dueAmount: 0,
  totalBeforeDiscount: 100,
  totalDiscountAmount: 0,
  totalAmount: 100,
  totalTaxAmount: 0,
  creditNoteAppliedAmount: 0,
  creditNoteRedemptions: [],
  warnings: [],
  document: null,
  isLegacyDocument: true,
  items: [
    {
      saleItemId: 'sale-item-one',
      lineType: 'Goods',
      itemId: 'item-one',
      serviceId: null,
      itemName: 'Milk',
      lineCode: 'MILK',
      inventoryBatchId: batch.id,
      quantity: 2,
      salesPrice: 50,
      originalSalesPrice: 50,
      finalSalesPrice: 50,
      preTaxAmountBeforeDiscount: 100,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: 100,
      taxAmount: 0,
      totalAmount: 100,
      savingsAmount: 0,
      taxRatePercent: 0,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 1,
      returnableQuantity: 1,
      returnStatus: 'PartiallyReturned',
      hsnCode: '0401',
      uom: 'kg',
      quantityScale: 3,
    },
  ],
  returns: [
    {
      saleReturnId: 'return-one',
      returnNumber: 'SR-1',
      returnedAt: new Date().toISOString(),
      totalRefundAmount: 50,
      dueReductionAmount: 0,
      payoutAmount: 50,
      payoutDestination: 'Refund',
      status: 'Completed',
      isVoided: false,
      voidReason: null,
      notes: null,
      creditNote: null,
      items: [
        {
          saleItemId: 'sale-item-one',
          quantity: 1,
          condition: 1,
          approvedRefundAmount: 50,
          quarantinedQuantity: 1,
          quarantineChangeId: 'root-held',
          dispositionReasonCode: 'Expiry.Expired',
        },
      ],
    },
  ],
};
async function capture(page: Page, options: { path: string; fullPage?: boolean }) {
  // Settle finite UI transitions without hiding content, disabling animations or masking app warnings.
  await page.evaluate(async () => {
    await Promise.all(document.getAnimations().filter(a => a.effect?.getTiming().iterations !== Infinity).map(a => a.finished.catch(() => {})));
  });
  await page.screenshot({ ...options, fullPage: false });
}
async function fixtures(page: Page) {
  const controls = { dispositionMode: 'conflict', posts: [] as unknown[] };
  await page.route('**/api/**', async (r) => {
    const path = new URL(r.request().url()).pathname;
    if (path === '/api/expiry-policies/readiness')
      return json(r, {
        businessDate: today(),
        catalogVersion: 3,
        results: {
          totalLots: 2,
          missingDateLots: 1,
          expiredLots: 1,
          quarantinedLots: 1,
          physicalQuantity: 20,
          quarantinedQuantity: 3,
          items: [
            {
              ...batch,
              batchId: batch.id,
              physicalQuantity: 10,
              missingDate: false,
              expired: true,
            },
          ],
        },
      });
    if (path.startsWith('/api/expiry-policies')) return json(r, state);
    if (path === `/api/shops/${SHOP_A}`)
      return json(r, {
        shopId: SHOP_A,
        name: 'Shop A',
        address: 'Market Road',
        city: 'Bengaluru',
        state: 'Karnataka',
        pincode: '560001',
        contactPerson: 'Owner',
        mobileNumber: '9999999999',
        gstNumber: null,
        bankName: null,
        bankAccountNumber: null,
        bankAccountType: null,
        ifscCode: null,
        accountHolderName: null,
        businessTimeZoneId: 'Asia/Kolkata',
        taxProfile: {
          taxRegime: 'Unregistered',
          legalName: 'Shop A',
          stateCode: '29',
          invoiceSeriesCode: 'A',
          eInvoiceStatus: 'BelowThreshold',
          eInvoiceAttestedAt: null,
        },
      });
    if (path === '/api/inventory/batches') return json(r, [batch]);
    if (path.endsWith('/dispositions')) {
      if (r.request().method() !== 'POST') return json(r, history);
      controls.posts.push(r.request().postDataJSON());
      return controls.dispositionMode === 'unknown'
        ? r.abort('failed')
        : json(r, { code: 'Inventory.BatchVersionConflict' }, 409);
    }
    if (path.includes('/financial-operations/batch-disposition/'))
      return json(r, { code: 'Operation.NotFound' }, 404);
    if (path === '/api/sales')
      return json(r, {
        items: [
          {
            ...sale,
            itemCount: 1,
            returnNumbers: ['SR-1'],
            status: 'partiallyReturned',
            refundAmount: 50,
            dueReductionAmount: 0,
          },
        ],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 20,
        summary: { periodSales: 100, invoiceCount: 1, refundAmount: 50 },
      });
    if (path === '/api/sales/sale-one') return json(r, sale);
    if (path === '/api/sales/sale-one/returns/preview')
      return json(r, {
        saleId: sale.saleId,
        hasFinancialAccess: true,
        warnings: [],
        financial: {
          totalRefundAmount: 50,
          dueReductionAmount: 0,
          payoutAmount: 50,
          totalTaxableAmount: 50,
          totalTaxAmount: 0,
          customerBalanceBefore: null,
          customerBalanceAfter: null,
        },
        lines: [
          {
            saleItemId: 'sale-item-one',
            itemId: 'item-one',
            inventoryBatchId: batch.id,
            requestedQuantity: 1,
            returnedQuantity: 1,
            returnableQuantity: 1,
            condition: 1,
            willRestock: true,
            physicalRestorationQuantity: 1,
            quarantinedQuantity: 1,
            dispositionReasonCode: 'Expiry.Expired',
            financial: {
              originalCostPrice: 40,
              originalSalesPrice: 50,
              originalTaxRatePercent: 0,
              originalIsPriceIncludingTax: false,
              maxRefundAmount: 50,
              approvedRefundAmount: 50,
              taxableAmount: 50,
              taxAmount: 0,
            },
          },
        ],
      });
    if (path === '/api/sales/sellables') return json(r, sellables);
    if (path === '/api/tills/capabilities')
      return json(r, {
        enabled: false,
        mixedTenderEnabled: false,
        businessDate: today(),
        businessTimeZoneId: 'Asia/Kolkata',
      });
    if (path === '/api/sales/preview') {
      const req = r.request().postDataJSON();
      const total = req.items.reduce(
        (n: number, x: { quantity: number }) => n + x.quantity * 50,
        0,
      );
      return json(r, {
        totalAmount: total,
        totalTaxableAmount: total,
        totalTaxAmount: 0,
        totalDiscountAmount: 0,
        saleLevelEligibleSubtotal: total,
        configuredSaleRule: null,
        infos: [],
        warnings: [],
        lines: req.items.map(
          (x: { inventoryBatchId: string; clientLineKey: string; quantity: number }) => ({
            ...x,
            itemId: 'item-one',
            lineType: 'Goods',
            barcode: 'MILK',
            itemName: 'Milk',
            batchNumber: 'EXPIRED-WARN',
            costPrice: 40,
            salesPrice: 50,
            mrp: 60,
            taxRatePercent: 0,
            isPriceIncludingTax: false,
            preTaxAmountBeforeDiscount: x.quantity * 50,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: x.quantity * 50,
            taxAmount: 0,
            lineTotalAmount: x.quantity * 50,
            maxAllowedItemDiscountFlat: 10,
            maxAllowedItemDiscountPercent: 20,
            configuredBatchRuleId: null,
            configuredBatchRulePercentage: null,
            hasClientPriceMismatch: false,
          }),
        ),
        expiryDecisions: req.items.map((x: { inventoryBatchId: string; quantity: number }) => ({
          itemId: 'item-one',
          inventoryBatchId: x.inventoryBatchId,
          quantity: x.quantity,
          batchVersion: 8,
          decision: decision(true, true),
        })),
      });
    }
    if (path === '/api/items/details')
      return json(r, {
        name: 'Milk',
        description: '',
        uom: 'kg',
        costPrice: 40,
        mrp: 60,
        salesPrice: 50,
        supplierId: null,
        supplierName: null,
        hsnCode: '0401',
        taxIncluded: false,
        taxRatePercent: 0,
        purchaseTaxIncluded: false,
      });
    if (path === '/api/customers') return json(r, []);
    return r.fallback();
  });
  return controls;
}
for (const width of [1440, 390]) {
  test(`expiry operator workflows at ${width}px`, async ({ page }) => {
    const server = await startPurchaseReceivingServer(
      resolve(__dirname, '../../../dist/INVENTORY/browser'),
    );
    await mkdir(OUTPUT, { recursive: true });
    page.setDefaultTimeout(12000);
    await page.setViewportSize({ width, height: 1000 });
    await page.addInitScript(
      (value) => localStorage.setItem('inventory.auth.session.local', JSON.stringify(value)),
      session(),
    );
    const controls = await fixtures(page);
    try {
      await page.goto(`${server.origin}/inventory/batches`);
      await expect(
        page.getByText('RETURN-LOT', { exact: true }).filter({ visible: true }).first(),
      ).toBeVisible();
      await capture(page, { path: `${OUTPUT}/${width}-lot-statuses.png`, fullPage: true });
      await page
        .getByRole('button', { name: 'Quarantine and history', exact: true })
        .filter({ visible: true })
        .first()
        .click();
      await expect(page.locator('app-batch-disposition-dialog li').first()).toContainText(
        'Customer return',
      );
      await page.getByLabel('Original quarantine event').selectOption('root-held');
      await page
        .getByLabel('Reason (required, up to 500 characters)')
        .fill('Confirmed damaged packaging');
      await page.getByRole('button', { name: 'Record action', exact: true }).click();
      await expect(
        page.getByText('The lot or source changed, or this action was refused.', { exact: false }),
      ).toBeVisible();
      await capture(page, {
        path: `${OUTPUT}/${width}-disposition-conflict-history.png`,
        fullPage: true,
      });
      controls.dispositionMode = 'unknown';
      await page.getByRole('button', { name: 'Record action', exact: true }).click();
      await expect(
        page.getByRole('button', { name: 'Recover the original action', exact: true }),
      ).toBeVisible();
      const originalUnknown = controls.posts[1];
      await page.reload();
      await page
        .getByRole('button', { name: 'Quarantine and history', exact: true })
        .filter({ visible: true })
        .first()
        .click();
      await page.getByRole('button', { name: 'Recover the original action', exact: true }).click();
      await expect.poll(() => controls.posts.length).toBe(3);
      expect(controls.posts[2]).toEqual(originalUnknown);
      await capture(page, {
        path: `${OUTPUT}/${width}-disposition-unknown-recovery.png`,
        fullPage: true,
      });
      await page.goto(`${server.origin}/sales/new`);
      await page.getByPlaceholder('Search products', { exact: true }).fill('MILK');
      await page.getByRole('button', { name: /Search$/ }).click();
      await expect(page.getByText('Required expiry date missing', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: /NO-DATE/ })).toHaveAttribute(
        'aria-disabled',
        'true',
      );
      await expect(page.getByRole('button', { name: /Add to Cart$/ })).toHaveCount(0);
      await capture(page, { path: `${OUTPUT}/${width}-blocked-picker.png`, fullPage: true });
      await page.getByText('EXPIRED-WARN', { exact: true }).click();
      await page.getByRole('button', { name: /Add to Cart$/ }).click();
      await expect(
        page.getByRole('heading', { name: 'Expired lots require approval' }),
      ).toBeVisible();
      await page
        .getByLabel('Reason (required, up to 500 characters)')
        .fill('Inspected with customer');
      await capture(page, { path: `${OUTPUT}/${width}-warning-reason.png`, fullPage: true });
      await page.goto(
        `${server.origin}/inventory/purchase-orders/10000000-0000-4000-8000-000000000001`,
      );
      await page
        .getByRole('button', { name: /Receive$/ })
        .first()
        .click();
      const receipt = page.getByRole('dialog', { name: 'Receive purchase order', exact: true });
      await receipt.locator('app-inventory-barcode-field input').first().fill('MILK');
      for (const [name, value] of Object.entries({ quantity: '1', totalPurchaseCost: '40', mrp: '60', salesPrice: '50' })) {
        const field = receipt.locator(`[formControlName="${name}"] input`).first(); await field.fill(value); await field.press('Tab');
      }
      for (const [name, value] of Object.entries({ manufacturingDate: '10/11/2026', expiryDate: '09/11/2026' })) {
        const field = receipt.locator(`[formControlName="${name}"] input`).first(); await field.fill(''); await field.pressSequentially(value); await field.press('Tab');
      }
      await page
        .getByRole('button', { name: /Receive$/ })
        .last()
        .click();
      await expect(
        page.getByText('Manufacturing date must be on or before expiry date.', { exact: true }),
      ).toBeVisible();
      await page.getByText('Manufacturing date must be on or before expiry date.', { exact: true }).scrollIntoViewIfNeeded();
      await capture(page, { path: `${OUTPUT}/${width}-receipt-date-error.png`, fullPage: true });
      await receipt.locator('[formControlName="expiryDate"] input').first().fill('');
      await receipt.locator('[formControlName="expiryDate"] input').first().press('Tab');
      await page
        .getByRole('button', { name: /Receive$/ })
        .last()
        .click();
      await expect(
        page.getByText('An expiry date is required for this item.', { exact: true }),
      ).toBeVisible();
      await page.getByText('An expiry date is required for this item.', { exact: true }).scrollIntoViewIfNeeded();
      await capture(page, {
        path: `${OUTPUT}/${width}-receipt-required-error.png`,
        fullPage: true,
      });
      await page.goto(`${server.origin}/sales`);
      await page
        .getByRole('button', { name: /View receipt/i })
        .first()
        .click();
      await expect(
        page.getByText('Recorded return: 1 units restored to quarantine.', { exact: false }),
      ).toBeVisible();
      await page.getByText('Recorded return: 1 units restored to quarantine.', { exact: false }).scrollIntoViewIfNeeded();
      await capture(page, { path: `${OUTPUT}/${width}-saved-return.png`, fullPage: true });
      await page.getByRole('button', { name: /Return items$/ }).click();
      await page.locator('app-sale-return-preview-dialog input[type="checkbox"]').first().check();
      await page.locator('app-sale-return-preview-dialog .return-preview-select').first().click();
      await page.getByRole('option', { name: 'Restockable', exact: true }).click();
      await page.getByRole('button', { name: /Preview refund$/ }).click();
      await expect(
        page.getByText('Restore 1 physical units; 1 units go to quarantine and cannot be sold.', {
          exact: true,
        }),
      ).toBeVisible();
      await page.getByText('Restore 1 physical units; 1 units go to quarantine and cannot be sold.', { exact: true }).scrollIntoViewIfNeeded();
      await capture(page, { path: `${OUTPUT}/${width}-return-preview.png`, fullPage: true });
      await page.goto(`${server.origin}/inventory/batches`);
      const expand = page.getByRole('button', { name: 'Expand sidebar', exact: true });
      if (await expand.isVisible()) await expand.click();
      if (width < 700) { await page.locator('.mobile-menu-trigger').click(); await page.locator('.mobile-profile-section .p-panelmenu-header').click(); }
      else await page.locator('app-sidebar-profile-menu .p-panelmenu-header').click();
      await page.getByText('Manage Shop', { exact: true }).filter({ visible: true }).first().click();
      await expect(page.getByRole('heading', { name: 'Expiry policy', exact: true })).toBeVisible();
      await page.locator('app-expiry-policy-form').scrollIntoViewIfNeeded();
      await capture(page, { path: `${OUTPUT}/${width}-policy-readiness.png`, fullPage: true });
    } finally {
      await server.close();
    }
  });
}
