import type { Page, Route } from '@playwright/test';

import type {
  PurchaseOrderDetail,
  PurchaseOrderListItem,
} from '../../../src/app/features/purchase-orders/services/purchase-order.service';
import {
  createShellScenario,
  installShellFixture,
  type ShellScenario,
  type ShellScenarioOptions,
} from './shell.fixture';

const API_BASE = 'http://localhost:5277/api';

export type PurchaseOrdersApiState = 'ready' | 'loading' | 'error';

export interface PurchaseOrdersScenario {
  readonly shell: ShellScenario;
  readonly orders: readonly PurchaseOrderListItem[];
  readonly apiState: PurchaseOrdersApiState;
  readonly withLongLines: boolean;
  readonly withReceiptHistory: boolean;
}

export interface PurchaseOrdersScenarioOptions extends ShellScenarioOptions {
  readonly orders?: readonly PurchaseOrderListItem[];
  readonly apiState?: PurchaseOrdersApiState;
  readonly withLongLines?: boolean;
  readonly withReceiptHistory?: boolean;
}

export const PURCHASE_ORDER_STATUSES: readonly PurchaseOrderListItem[] = [
  order('po-draft', 'Draft', { createdAt: '2026-06-01T10:00:00.000Z' }),
  order('po-placed', 'Placed', { createdAt: '2026-06-15T10:00:00.000Z' }),
  order('po-partial', 'PartiallyReceived', {
    createdAt: '2026-06-30T10:00:00.000Z',
    receivedQuantity: 3,
  }),
  order('po-received', 'Received', { createdAt: '2026-07-01T10:00:00.000Z', receivedQuantity: 8 }),
  order('po-closed', 'Closed', { createdAt: '2026-07-15T10:00:00.000Z', receivedQuantity: 5 }),
  order('po-cancelled', 'Cancelled', { createdAt: '2026-07-30T10:00:00.000Z' }),
];

export const LONG_PURCHASE_ORDER = order('po-long-values', 'Placed', {
  purchaseOrderNumber: 'PO-2026-LONG-REFERENCE-000001',
  supplierName: 'Very Long International Supplier Name With Deterministic Audit Value',
  supplierReference: 'SUPPLIER-REFERENCE-WITH-A-LONG-DETERMINISTIC-VALUE-2026-000001',
});

export const DENSE_PURCHASE_ORDERS = Array.from({ length: 40 }, (_, index) =>
  order(
    `po-dense-${index + 1}`,
    PURCHASE_ORDER_STATUSES[index % PURCHASE_ORDER_STATUSES.length].status,
    {
      purchaseOrderNumber: `PO-2026-${String(index + 1).padStart(6, '0')}`,
    },
  ),
);

export function createPurchaseOrdersScenario(
  options: PurchaseOrdersScenarioOptions = {},
): PurchaseOrdersScenario {
  return {
    shell: createShellScenario(options),
    orders: options.orders ?? PURCHASE_ORDER_STATUSES,
    apiState: options.apiState ?? 'ready',
    withLongLines: options.withLongLines ?? false,
    withReceiptHistory: options.withReceiptHistory ?? false,
  };
}

export async function installPurchaseOrdersFixture(
  page: Page,
  scenario: PurchaseOrdersScenario,
): Promise<void> {
  await installShellFixture(page, scenario.shell);
  const state: PurchaseOrdersFixtureState = {
    apiState: scenario.apiState,
    orders: [...scenario.orders],
    withLongLines: scenario.withLongLines,
    withReceiptHistory: scenario.withReceiptHistory,
  };
  await page.route(`${API_BASE}/purchase-orders**`, (route) =>
    handlePurchaseOrderRoute(route, state),
  );
}

interface PurchaseOrdersFixtureState {
  readonly apiState: PurchaseOrdersApiState;
  orders: PurchaseOrderListItem[];
  readonly withLongLines: boolean;
  readonly withReceiptHistory: boolean;
}

function handlePurchaseOrderRoute(route: Route, state: PurchaseOrdersFixtureState): Promise<void> {
  if (state.apiState === 'loading') {
    return new Promise(() => undefined);
  }
  if (state.apiState === 'error') {
    return fulfillJson(route, { title: 'PurchaseOrder.LoadFailed' }, 503);
  }

  const url = new URL(route.request().url());
  const segments = url.pathname.split('/').filter(Boolean);
  if (route.request().method() === 'GET' && segments.length === 3) {
    const order = state.orders.find((item) => item.purchaseOrderId === segments.at(-1));
    return fulfillJson(
      route,
      order ? toDetail(order, { withLongLines: state.withLongLines, withReceiptHistory: state.withReceiptHistory }) : { title: 'PurchaseOrder.NotFound' },
      order ? 200 : 404,
    );
  }
  if (route.request().method() === 'POST' && segments.length === 4 && segments.at(-1) === 'place') {
    return placeOrder(route, state, segments[2]);
  }
  if (route.request().method() === 'DELETE' && segments.length === 3) {
    return deleteOrder(route, state, segments[2]);
  }

  return fulfillJson(route, listResult(state.orders, url.searchParams));
}

function listResult(orders: readonly PurchaseOrderListItem[], params: URLSearchParams) {
  const status = params.get('status') ?? '';
  const search = (params.get('search') ?? '').toLocaleLowerCase();
  const orderDateFrom = params.get('order_date_from') ?? '';
  const orderDateTo = params.get('order_date_to') ?? '';
  const page = Number(params.get('page') ?? '1');
  const pageSize = Number(params.get('page_size') ?? '20');
  const matching = orders.filter(
    (order) =>
      (!status || order.status === status) &&
      (!search || searchableText(order).includes(search)) &&
      (!orderDateFrom || orderDate(order) >= orderDateFrom) &&
      (!orderDateTo || orderDate(order) <= orderDateTo),
  );
  const start = (page - 1) * pageSize;

  return {
    items: matching.slice(start, start + pageSize),
    totalCount: matching.length,
    pageNumber: page,
    pageSize,
  };
}

function placeOrder(
  route: Route,
  state: PurchaseOrdersFixtureState,
  purchaseOrderId: string | undefined,
): Promise<void> {
  const order = state.orders.find((item) => item.purchaseOrderId === purchaseOrderId);
  if (!order || order.status !== 'Draft') {
    return fulfillJson(route, { title: 'PurchaseOrder.CannotPlaceNonDraft' }, 422);
  }

  const placedOrder = { ...order, status: 'Placed' as const };
  state.orders = state.orders.map((item) =>
    item.purchaseOrderId === purchaseOrderId ? placedOrder : item,
  );
  return fulfillJson(route, toDetail(placedOrder));
}

function deleteOrder(
  route: Route,
  state: PurchaseOrdersFixtureState,
  purchaseOrderId: string | undefined,
): Promise<void> {
  const order = state.orders.find((item) => item.purchaseOrderId === purchaseOrderId);
  if (!order || order.status !== 'Draft') {
    return fulfillJson(route, { title: 'PurchaseOrder.CannotDeleteNonDraft' }, 422);
  }

  state.orders = state.orders.filter((item) => item.purchaseOrderId !== purchaseOrderId);
  return route.fulfill({ status: 204 });
}

function searchableText(order: PurchaseOrderListItem): string {
  return [order.purchaseOrderNumber, order.supplierName, order.supplierReference]
    .filter((value): value is string => !!value)
    .join(' ')
    .toLocaleLowerCase();
}

function orderDate(order: PurchaseOrderListItem): string {
  return order.createdAt.slice(0, 10);
}

function toDetail(order: PurchaseOrderListItem, opts: { withLongLines?: boolean; withReceiptHistory?: boolean } = {}): PurchaseOrderDetail {
  const detail: PurchaseOrderDetail = {
    purchaseOrderId: order.purchaseOrderId,
    purchaseOrderNumber: order.purchaseOrderNumber,
    status: order.status,
    supplierId: null,
    supplierName: order.supplierName,
    supplierReference: order.supplierReference,
    receivedQuantity: order.receivedQuantity,
    orderDate: orderDate(order),
    expectedDeliveryDate: null,
    supplierReferenceNumber: order.supplierReference,
    notes: null,
    lines: opts.withLongLines ? generateLongLines() : [],
    expectedTotal: order.expectedTotal,
    createdAt: order.createdAt,
    cancellationReason: null,
  };

  if (opts.withReceiptHistory) {
    detail.receipts = generateReceipts();
  }

  return detail;
}

function generateLongLines() {
  return [
    {
      lineId: 'line-1',
      itemId: 'item-1',
      description: 'Very Long Description: This is a comprehensive line item description that spans multiple words and could potentially wrap in certain layouts.',
      expectedQuantity: 100,
      receivedQuantity: 75,
      remainingQuantity: 25,
      unitCost: 125.5,
      lineTotal: 12550,
    },
    {
      lineId: 'line-2',
      itemId: 'item-2',
      description: 'Another Extended Line Item With Additional Context Information for Audit Purposes',
      expectedQuantity: 50,
      receivedQuantity: 50,
      remainingQuantity: 0,
      unitCost: 250.0,
      lineTotal: 12500,
    },
  ];
}

function generateReceipts() {
  return [
    {
      receiptId: 'receipt-1',
      receiptNumber: 'REC-2026-001',
      receivedAt: '2026-07-15T10:30:00.000Z',
      referenceNumber: 'REF-001',
      notes: 'First partial receipt',
      receivedByUserId: 'user-1',
      receivedByDisplayName: 'John Doe',
      lines: [
        {
          receiptLineId: 'recline-1',
          purchaseOrderLineId: 'line-1',
          itemId: 'item-1',
          inventoryBatchId: 'batch-1',
          batchNumber: 'BATCH-2026-001',
          batchVoided: false,
          stockTransactionId: 'tx-1',
          quantity: 50,
          totalPurchaseCost: 6275,
          unitCost: 125.5,
          mrp: 150.0,
          salesPrice: 155.0,
          taxRatePercent: 10,
          taxIncluded: true,
          purchaseTaxIncluded: false,
        },
      ],
    },
    {
      receiptId: 'receipt-2',
      receiptNumber: 'REC-2026-002',
      receivedAt: '2026-07-20T14:00:00.000Z',
      referenceNumber: 'REF-002',
      notes: 'Second partial receipt',
      receivedByUserId: 'user-2',
      receivedByDisplayName: 'Jane Smith',
      lines: [
        {
          receiptLineId: 'recline-2',
          purchaseOrderLineId: 'line-1',
          itemId: 'item-1',
          inventoryBatchId: 'batch-2',
          batchNumber: 'BATCH-2026-002',
          batchVoided: false,
          stockTransactionId: 'tx-2',
          quantity: 25,
          totalPurchaseCost: 3137.5,
          unitCost: 125.5,
          mrp: 150.0,
          salesPrice: 155.0,
          taxRatePercent: 10,
          taxIncluded: true,
          purchaseTaxIncluded: false,
        },
      ],
    },
  ];
}

function order(
  purchaseOrderId: string,
  status: PurchaseOrderListItem['status'],
  overrides: Partial<PurchaseOrderListItem> = {},
): PurchaseOrderListItem {
  return {
    purchaseOrderId,
    purchaseOrderNumber: `PO-2026-${purchaseOrderId}`,
    status,
    supplierName: 'Audit Supplier',
    supplierReference: 'AUDIT-REF-001',
    lineCount: 3,
    expectedQuantity: 8,
    receivedQuantity: 0,
    expectedTotal: 1250,
    createdAt: '2026-07-01T10:00:00.000Z',
    ...overrides,
  };
}

function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
