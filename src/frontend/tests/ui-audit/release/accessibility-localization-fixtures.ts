import type { Page, Route } from '@playwright/test';
import { SHOP_A, USER } from './purchase-receiving-server';

export const LONG_NAME =
  'Premium traditional household provisions — परिवार சந்தை కుటుంబം ಕನ್ನಡ বাংলা मराठी ગુજરાતી';
export const CUSTOMER = {
  customerId: 'customer-a11y',
  name: LONG_NAME,
  phoneNumber: '9999999999',
  creditLimit: 100000,
  outstandingDue: 50,
  isActive: true,
  creditPolicyMode: 'LegacyAdvisory',
  creditTermsDays: null,
};
const json = (r: Route, body: unknown, status = 200) =>
  r.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
const currentBusinessDate = () => {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en', {
      timeZone: 'Asia/Kolkata',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    })
      .formatToParts(new Date())
      .map((part) => [part.type, part.value]),
  );
  return `${parts['year']}-${parts['month']}-${parts['day']}`;
};
const decision = {
  allowed: true,
  reasonCode: null,
  warning: false,
  requiresApproval: false,
  policyVersion: 1,
  catalogVersion: 1,
  businessDate: currentBusinessDate(),
  physicalQuantity: 100,
  quarantinedQuantity: 0,
  availableQuantity: 100,
};
export const GOODS = [0, 1].map((i) => ({
  kind: 'Goods',
  inventoryBatchId: `batch-a11y-${i}`,
  itemId: 'item-1',
  barcode: 'A11Y',
  itemName: LONG_NAME,
  batchNumber: 'DUPLICATE-LOT',
  quantity: 100,
  availableQuantity: 100,
  quarantinedQuantity: 0,
  costPrice: 40,
  salesPrice: 50,
  mrp: 60,
  taxRatePercent: 0,
  taxIncluded: false,
  expiryDate: '2099-12-31',
  version: 8,
  quantityScale: 3,
  uom: 'kg',
  sellability: decision,
}));
export const SALE = {
  saleId: 'sale-a11y',
  invoiceNumber: 'A/2627/000123',
  customerId: CUSTOMER.customerId,
  customerName: LONG_NAME,
  customerPhone: CUSTOMER.phoneNumber,
  paymentMethod: 1,
  soldAt: '2026-11-09T06:00:00Z',
  businessDate: '2026-11-09',
  paidAmount: 50,
  dueAmount: 50,
  totalBeforeDiscount: 100,
  totalDiscountAmount: 0,
  totalAmount: 100,
  totalTaxAmount: 0,
  creditNoteAppliedAmount: 0,
  creditNoteRedemptions: [],
  warnings: [],
  isLegacyDocument: false,
  returns: [],
  items: [
    {
      saleItemId: 'line-a11y',
      lineType: 'Goods',
      itemId: 'item-1',
      itemName: LONG_NAME,
      lineCode: 'A11Y',
      inventoryBatchId: GOODS[0].inventoryBatchId,
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
      returnedQuantity: 0,
      returnableQuantity: 2,
      returnStatus: 'None',
      hsnCode: null,
      uom: 'kg',
      quantityScale: 3,
    },
  ],
  document: {
    version: 2,
    documentType: 'Receipt',
    issuedAt: '2026-11-09T06:00:00Z',
    businessDate: '2026-11-09',
    fiscalYearStart: 2026,
    seriesCode: 'A',
    number: 'A/2627/000123',
    seller: {
      legalName: 'Frozen Market परिवार சந்தை',
      tradeName: null,
      address: '12 Market Road, Bengaluru',
      stateCode: '29',
      stateName: 'Karnataka',
      gstin: null,
      taxRegime: 'Unregistered',
    },
    buyer: {
      name: LONG_NAME,
      phone: CUSTOMER.phoneNumber,
      address: 'Long residential address, Market Road, Bengaluru',
      gstin: null,
      stateCode: '29',
      stateName: 'Karnataka',
    },
    placeOfSupplyStateCode: '29',
    placeOfSupplyStateName: 'Karnataka',
    isInterstate: false,
    reverseCharge: false,
    lines: [
      {
        lineNumber: 1,
        lineType: 'Goods',
        description: LONG_NAME,
        hsnOrSac: null,
        uom: 'kg',
        quantity: 2,
        unitPrice: 50,
        discountAmount: 0,
        taxableValue: 100,
        taxRatePercent: 0,
        isExempt: true,
        taxComponents: [],
        lineTotal: 100,
      },
    ],
    totals: {
      taxableValue: 0,
      exemptValue: 100,
      components: [],
      totalTax: 0,
      roundingAdjustment: 0,
      invoiceTotal: 100,
    },
    declarations: [],
    signatureMode: 'AuthorisedSignatory',
    taxCalculation: 'component-split/1',
  },
};
export async function accessibilityFixtures(page: Page) {
  const controls = {
    saleMode: 'accept' as 'accept' | 'drop' | 'hold',
    paymentMode: 'accept' as 'accept' | 'drop' | 'hold' | 'reject',
    salePosts: [] as Record<string, any>[],
    paymentPosts: [] as Record<string, any>[],
    held: [] as (() => Promise<void>)[],
  };
  await page.context().route('**/api/**', async (r) => {
    const path = new URL(r.request().url()).pathname;
    const currentDecision = { ...decision, businessDate: currentBusinessDate() };
    if (path === '/api/items/stream')
      return r.fulfill({ contentType: 'text/event-stream', body: '' });
    if (path === '/api/tills/capabilities')
      return json(r, {
        enabled: false,
        mixedTenderEnabled: false,
        businessDate: currentDecision.businessDate,
        businessTimeZoneId: 'Asia/Kolkata',
      });
    if (path === `/api/shops/${SHOP_A}`)
      return json(r, {
        shopId: SHOP_A,
        name: 'Live shop must not replace frozen seller',
        address: 'Market Road',
        city: 'Bengaluru',
        state: 'Karnataka',
        pincode: '560001',
        mobileNumber: '9999999999',
        businessTimeZoneId: 'Asia/Kolkata',
        taxProfile: {
          taxRegime: 'Unregistered',
          legalName: 'Fixture shop',
          stateCode: '29',
          invoiceSeriesCode: 'A',
          eInvoiceStatus: 'BelowThreshold',
        },
      });
    if (path === '/api/sales/sellables')
      return json(
        r,
        new URL(r.request().url()).searchParams.get('searchTerm') === 'NO-MATCH'
          ? []
          : GOODS.map((good) => ({ ...good, sellability: currentDecision })),
      );
    if (path === '/api/inventory/batches/available') return json(r, []);
    if (path === '/api/items/details')
      return json(r, { ...GOODS[0], name: LONG_NAME, hsnCode: null, purchaseTaxIncluded: false });
    if (path === '/api/customers') return json(r, [CUSTOMER]);
    if (path.endsWith('/credit') && path.includes('/customers/'))
      return json(r, {
        ...CUSTOMER,
        currentExposure: 50,
        availableCredit: null,
        overdueAmount: 0,
        requiresOverride: false,
      });
    if (path.endsWith('/account'))
      return json(r, {
        ...CUSTOMER,
        unallocatedCredit: 0,
        sales: [
          {
            ...SALE,
            currentOutstandingAmount: 50,
            allocatedPaymentAmount: 0,
            returnCreditAmount: 0,
            dueDate: '2026-11-10',
          },
        ],
        ledgerEntries: [
          {
            entryId: 'entry-a11y',
            entryType: 'Sale',
            saleId: SALE.saleId,
            amount: 50,
            entryDate: '2026-11-09',
            runningBalance: 50,
            notes: null,
          },
        ],
        paymentHistory: [],
        receipts: [],
        openingBalance: 0,
        closingBalance: 50,
        asOfBusinessDate: '2026-11-09',
        overdueAmount: 0,
      });
    if (path.endsWith('/payments') && r.request().method() === 'POST') {
      controls.paymentPosts.push(r.request().postDataJSON());
      if (controls.paymentMode === 'reject')
        return json(
          r,
          {
            code: 'Validation.Failed',
            errors: { Amount: ['unsafe raw message'] },
            traceId: 'a11y-reference-1',
          },
          400,
        );
      if (controls.paymentMode === 'drop') return r.abort('failed');
      const done = () =>
        json(r, { receiptId: 'receipt-a11y', amount: 5, ledgerEntryId: 'entry-payment' });
      if (controls.paymentMode === 'hold') {
        controls.held.push(done);
        return;
      }
      return done();
    }
    if (path.startsWith('/api/financial-operations/customer-payment/'))
      return json(r, { code: 'Operation.NotFound' }, 404);
    if (path === '/api/sales/preview') {
      const req = r.request().postDataJSON();
      const total = req.items.reduce((n: number, x: any) => n + x.quantity * 50, 0);
      return json(r, {
        totalAmount: total,
        totalTaxableAmount: total,
        totalTaxAmount: 0,
        totalDiscountAmount: 0,
        saleLevelEligibleSubtotal: total,
        configuredSaleRule: null,
        infos: [],
        warnings: [],
        lines: req.items.map((x: any) => ({
          ...x,
          itemId: 'item-1',
          lineType: 'Goods',
          barcode: 'A11Y',
          itemName: LONG_NAME,
          batchNumber: 'DUPLICATE-LOT',
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
        })),
        expiryDecisions: req.items.map((x: any) => ({
          itemId: 'item-1',
          inventoryBatchId: x.inventoryBatchId,
          quantity: x.quantity,
          batchVersion: 8,
          decision: currentDecision,
        })),
      });
    }
    if (path === '/api/sales' && r.request().method() === 'POST') {
      controls.salePosts.push(r.request().postDataJSON());
      if (controls.saleMode === 'drop') return r.abort('failed');
      const done = () => json(r, SALE);
      if (controls.saleMode === 'hold') {
        controls.held.push(done);
        return;
      }
      return done();
    }
    if (path === `/api/sales/${SALE.saleId}`) return json(r, SALE);
    if (path === '/api/offline-reconciliation')
      return json(r, {
        items: [
          {
            caseId: 'case-a11y',
            version: 1,
            invoiceNumber: 'A/2627/000124',
            clientSaleId: 'client-a11y',
            deviceId: 'device-a11y',
            issueType: 'ArithmeticConflict',
            status: 'Open',
            reasonCodes: ['ArithmeticConflict'],
            message: '',
            saleId: null,
            resolution: null,
            createdAt: '2026-11-09T06:00:00Z',
            createdBy: { userId: USER, displayName: LONG_NAME },
            resolvedAt: null,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        unresolvedCount: 1,
      });
    return r.fallback();
  });
  return controls;
}
