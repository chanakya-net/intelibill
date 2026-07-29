import type { Page, Route } from '@playwright/test';

import type { SaleDto } from '../../../src/app/features/sales/services/sale.models';
import {
  createShellScenario,
  installShellFixture,
  type ShellScenario,
  type ShellScenarioOptions,
} from './shell.fixture';

const API_BASE = 'http://localhost:5277/api';

export type SaleInvoiceApiState = 'ready' | 'loading' | 'error';

// Distinctive terminal token appended after the repeated sentences. Tests use
// it to prove the entire long description survives to a printed page — not
// just the opening sentence that happens to sit at the very start of the
// block — and to prove it lands on a later page than the opening sentence.
export const LONG_DESCRIPTION_TAIL_MARKER = 'ZZDETERMINISTICTAILMARKERZZ';

export const LONG_DESCRIPTION = `${Array.from(
  { length: 160 },
  () => 'Deterministic long invoice description for A4 pagination coverage.',
).join(' ')} ${LONG_DESCRIPTION_TAIL_MARKER}`;

export interface SaleInvoiceScenario {
  readonly shell: ShellScenario;
  readonly sales: readonly SaleDto[];
  readonly apiState: SaleInvoiceApiState;
}

export interface SaleInvoiceScenarioOptions extends ShellScenarioOptions {
  readonly sales?: readonly SaleDto[];
  readonly apiState?: SaleInvoiceApiState;
  readonly longAddress?: boolean;
  readonly optionalShopFields?: boolean;
}

export const NORMAL_SALE: SaleDto = {
  saleId: 'sale-normal',
  invoiceNumber: 'INV-2026-001',
  customerId: 'customer-1',
  customerName: 'John Doe',
  customerPhone: '+919876543210',
  paymentMethod: 1,
  soldAt: '2026-07-20T10:00:00Z',
  paidAmount: 5900,
  dueAmount: 0,
  totalBeforeDiscount: 5000,
  totalDiscountAmount: 0,
  totalAmount: 5900,
  totalTaxAmount: 900,
  creditNoteAppliedAmount: 0,
  items: [
    {
      saleItemId: 'item-1',
      lineType: 'Goods',
      itemId: 'item-1',
      serviceId: null,
      itemName: 'Premium Widget',
      lineCode: 'ITEM-001',
      inventoryBatchId: 'batch-1',
      quantity: 10,
      salesPrice: 500,
      originalSalesPrice: 500,
      finalSalesPrice: 500,
      preTaxAmountBeforeDiscount: 5000,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: 5000,
      taxAmount: 900,
      totalAmount: 5900,
      savingsAmount: 0,
      taxRatePercent: 18,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 10,
      returnStatus: 'Returnable',
      hsnCode: '1234567890',
    },
  ],
  returns: [],
  warnings: [],
};

export const LONG_ADDRESS_SALE: SaleDto = {
  ...NORMAL_SALE,
  saleId: 'sale-long-address',
  invoiceNumber: 'INV-2026-002',
  customerId: 'customer-2',
  customerName: 'Very Long Customer Name With Multiple Words For Audit Coverage',
  customerPhone: '+919876543210',
  items: [{ ...NORMAL_SALE.items[0], itemName: LONG_DESCRIPTION }],
};

export const DENSE_SALE: SaleDto = {
  saleId: 'sale-dense',
  invoiceNumber: 'INV-2026-DENSE-001',
  customerId: 'customer-dense',
  customerName: 'Dense Scenario Customer',
  customerPhone: '+919876543210',
  paymentMethod: 1,
  soldAt: '2026-07-20T10:00:00Z',
  paidAmount: 15750,
  dueAmount: 0,
  totalBeforeDiscount: 12000,
  totalDiscountAmount: 250,
  totalAmount: 15750,
  totalTaxAmount: 4000,
  creditNoteAppliedAmount: 0,
  items: [
    {
      saleItemId: 'item-dense-1',
      lineType: 'Goods',
      itemId: 'item-dense-1',
      serviceId: null,
      itemName: 'Item A',
      lineCode: 'ITEM-A001',
      inventoryBatchId: 'batch-1',
      quantity: 25,
      salesPrice: 100,
      originalSalesPrice: 100,
      finalSalesPrice: 100,
      preTaxAmountBeforeDiscount: 2500,
      itemDiscountAmount: 50,
      saleDiscountAmount: 0,
      taxableAmount: 2450,
      taxAmount: 441,
      totalAmount: 2891,
      savingsAmount: 0,
      taxRatePercent: 18,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 25,
      returnStatus: 'Returnable',
      hsnCode: '1234567890',
    },
    {
      saleItemId: 'item-dense-2',
      lineType: 'Goods',
      itemId: 'item-dense-2',
      serviceId: null,
      itemName: 'Item B',
      lineCode: 'ITEM-B001',
      inventoryBatchId: 'batch-2',
      quantity: 30,
      salesPrice: 200,
      originalSalesPrice: 200,
      finalSalesPrice: 200,
      preTaxAmountBeforeDiscount: 6000,
      itemDiscountAmount: 100,
      saleDiscountAmount: 100,
      taxableAmount: 5800,
      taxAmount: 1044,
      totalAmount: 6844,
      savingsAmount: 0,
      taxRatePercent: 18,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 30,
      returnStatus: 'Returnable',
      hsnCode: '1234567890',
    },
    {
      saleItemId: 'item-dense-3',
      lineType: 'Service',
      itemId: null,
      serviceId: 'service-1',
      itemName: 'Installation Service',
      lineCode: 'SVC-INST001',
      inventoryBatchId: null,
      quantity: 1,
      salesPrice: 3500,
      originalSalesPrice: 3500,
      finalSalesPrice: 3500,
      preTaxAmountBeforeDiscount: 3500,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: 3500,
      taxAmount: 630,
      totalAmount: 4130,
      savingsAmount: 0,
      taxRatePercent: 18,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 1,
      returnStatus: 'Returnable',
      hsnCode: null,
    },
  ],
  returns: [],
  warnings: [],
};

export const LARGE_VALUE_SALE: SaleDto = {
  saleId: 'sale-large',
  invoiceNumber: 'INV-2026-LARGE-001',
  customerId: 'customer-large',
  customerName: 'Premium Large Account Customer',
  customerPhone: '+919876543210',
  paymentMethod: 3,
  soldAt: '2026-07-20T10:00:00Z',
  paidAmount: 99999.9,
  dueAmount: 0,
  totalBeforeDiscount: 80000,
  totalDiscountAmount: 5000,
  totalAmount: 99999.9,
  totalTaxAmount: 25000,
  creditNoteAppliedAmount: 0,
  items: [
    {
      saleItemId: 'item-large-1',
      lineType: 'Goods',
      itemId: 'item-large-1',
      serviceId: null,
      itemName: 'Bulk Equipment',
      lineCode: 'ITEM-BULK001',
      inventoryBatchId: 'batch-bulk',
      quantity: 100,
      salesPrice: 800,
      originalSalesPrice: 800,
      finalSalesPrice: 800,
      preTaxAmountBeforeDiscount: 80000,
      itemDiscountAmount: 5000,
      saleDiscountAmount: 0,
      taxableAmount: 75000,
      taxAmount: 25000,
      totalAmount: 100000,
      savingsAmount: 0,
      taxRatePercent: 18,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 100,
      returnStatus: 'Returnable',
      hsnCode: '8704101090',
    },
  ],
  returns: [],
  warnings: [],
};

export const OPTIONAL_FIELDS_SALE: SaleDto = {
  saleId: 'sale-optional',
  invoiceNumber: 'INV-2026-OPT-001',
  customerId: null,
  customerName: null,
  customerPhone: null,
  paymentMethod: 1,
  soldAt: '2026-07-20T10:00:00Z',
  paidAmount: 1180,
  dueAmount: 0,
  totalBeforeDiscount: 1000,
  totalDiscountAmount: 0,
  totalAmount: 1180,
  totalTaxAmount: 180,
  creditNoteAppliedAmount: 0,
  items: [
    {
      saleItemId: 'item-opt-1',
      lineType: 'Goods',
      itemId: 'item-opt-1',
      serviceId: null,
      itemName: 'Basic Item',
      lineCode: 'ITEM-OPT001',
      inventoryBatchId: 'batch-opt',
      quantity: 1,
      salesPrice: 1000,
      originalSalesPrice: 1000,
      finalSalesPrice: 1000,
      preTaxAmountBeforeDiscount: 1000,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: 1000,
      taxAmount: 180,
      totalAmount: 1180,
      savingsAmount: 0,
      taxRatePercent: 18,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 1,
      returnStatus: 'Returnable',
      hsnCode: '1234567890',
    },
  ],
  returns: [],
  warnings: [],
};

export function createSaleInvoiceScenario(
  options: SaleInvoiceScenarioOptions = {},
): SaleInvoiceScenario {
  const baseShell = createShellScenario(options);
  const shell = withInvoiceShopDetails(baseShell, options);

  return {
    shell,
    sales: options.sales ?? [NORMAL_SALE],
    apiState: options.apiState ?? 'ready',
  };
}

function withInvoiceShopDetails(
  shell: ShellScenario,
  options: SaleInvoiceScenarioOptions,
): ShellScenario {
  if (!options.longAddress && !options.optionalShopFields) {
    return shell;
  }

  const shopDetails = Object.fromEntries(
    Object.entries(shell.shopDetails).map(([shopId, details]) => [
      shopId,
      {
        ...details,
        address: options.longAddress
          ? '123 Deterministic Audit Avenue, Building C, Floor 7, Long Address District'
          : details.address,
        city: options.longAddress ? 'Bengaluru Metropolitan Commerce Zone' : details.city,
        mobileNumber: options.optionalShopFields ? null : details.mobileNumber,
        gstNumber: options.optionalShopFields ? null : details.gstNumber,
      },
    ]),
  );

  return { ...shell, shopDetails };
}

export async function installSaleInvoiceFixture(
  page: Page,
  scenario: SaleInvoiceScenario,
): Promise<void> {
  await installShellFixture(page, scenario.shell);
  const state: SaleInvoiceFixtureState = {
    apiState: scenario.apiState,
    sales: new Map(scenario.sales.map((sale) => [sale.saleId, sale])),
  };
  await page.route(`${API_BASE}/sales/**`, (route) => handleSaleRoute(route, state));
}

interface SaleInvoiceFixtureState {
  readonly apiState: SaleInvoiceApiState;
  readonly sales: Map<string, SaleDto>;
}

function handleSaleRoute(route: Route, state: SaleInvoiceFixtureState): Promise<void> {
  if (state.apiState === 'loading') {
    return new Promise(() => undefined);
  }
  if (state.apiState === 'error') {
    return fulfillJson(route, { title: 'Sale.LoadFailed' }, 503);
  }

  const url = new URL(route.request().url());
  const segments = url.pathname.split('/').filter(Boolean);
  const pathIndex = segments.indexOf('sales');
  if (pathIndex === -1 || pathIndex + 1 >= segments.length) {
    return fulfillJson(route, { title: 'Sale.IdRequired' }, 400);
  }

  const saleId = decodeURIComponent(segments[pathIndex + 1]);
  const sale = state.sales.get(saleId);
  return fulfillJson(route, sale ?? { title: 'Sale.NotFound' }, sale ? 200 : 404);
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}
