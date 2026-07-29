import type { Page, Route } from '@playwright/test';

import type {
  PurchaseOrderDetail,
  PurchaseOrderListItem,
  ReceivePurchaseOrderRequest,
} from '../../../src/app/features/purchase-orders/services/purchase-order.service';
import {
  createShellScenario,
  installShellFixture,
  type ShellScenario,
  type ShellScenarioOptions,
} from './shell.fixture';
import {
  longPurchaseOrderLines,
  purchaseOrderReceiptHistory,
  denseLinesPurchaseOrder,
} from './purchase-orders-detail-data.fixture';

const API_BASE = 'http://localhost:5277/api';

export type PurchaseOrdersApiState = 'ready' | 'loading' | 'error';

export interface PurchaseOrdersScenario {
  readonly shell: ShellScenario;
  readonly orders: readonly PurchaseOrderListItem[];
  readonly apiState: PurchaseOrdersApiState;
  readonly withLongLines: boolean;
  readonly withReceiptHistory: boolean;
  readonly withLongSupplierDetails: boolean;
  readonly withLongNotes: boolean;
  readonly withDenseLines: boolean;
  readonly withLongShopAddress: boolean;
}

export interface PurchaseOrdersScenarioOptions extends ShellScenarioOptions {
  readonly orders?: readonly PurchaseOrderListItem[];
  readonly apiState?: PurchaseOrdersApiState;
  readonly withLongLines?: boolean;
  readonly withReceiptHistory?: boolean;
  readonly withLongSupplierDetails?: boolean;
  readonly withLongNotes?: boolean;
  readonly withDenseLines?: boolean;
  readonly withLongShopAddress?: boolean;
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
    withLongSupplierDetails: options.withLongSupplierDetails ?? false,
    withLongNotes: options.withLongNotes ?? false,
    withDenseLines: options.withDenseLines ?? false,
    withLongShopAddress: options.withLongShopAddress ?? false,
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
    details: new Map(
      scenario.orders.map((order) => [
        order.purchaseOrderId,
        toDetail(order, {
          withLongLines: scenario.withLongLines,
          withReceiptHistory: scenario.withReceiptHistory,
          withLongSupplierDetails: scenario.withLongSupplierDetails,
          withLongNotes: scenario.withLongNotes,
          withDenseLines: scenario.withDenseLines,
        }),
      ]),
    ),
  };
  await page.route(`${API_BASE}/hsn/lookup`, (route) =>
    fulfillJson(route, { hsnCodes: [], taxScenarios: [] }),
  );
  await page.route(`${API_BASE}/purchase-orders**`, (route) =>
    handlePurchaseOrderRoute(route, state),
  );
}

interface PurchaseOrdersFixtureState {
  readonly apiState: PurchaseOrdersApiState;
  orders: PurchaseOrderListItem[];
  readonly details: Map<string, PurchaseOrderDetail>;
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
    const order = state.details.get(segments.at(-1) ?? '');
    return fulfillJson(route, order ?? { title: 'PurchaseOrder.NotFound' }, order ? 200 : 404);
  }
  if (
    route.request().method() === 'POST' &&
    segments.length === 4 &&
    segments.at(-1) === 'receipts'
  ) {
    return receiveOrder(route, state, segments[2]);
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
  const detail = {
    ...toDetail(placedOrder),
    lines: state.details.get(placedOrder.purchaseOrderId)?.lines ?? [],
  };
  state.details.set(placedOrder.purchaseOrderId, detail);
  return fulfillJson(route, detail);
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
  state.details.delete(purchaseOrderId ?? '');
  return route.fulfill({ status: 204 });
}

function receiveOrder(
  route: Route,
  state: PurchaseOrdersFixtureState,
  purchaseOrderId: string | undefined,
): Promise<void> {
  const order = state.details.get(purchaseOrderId ?? '');
  const payload = route.request().postDataJSON() as ReceivePurchaseOrderRequest;
  if (!order || !canReceive(order, payload)) {
    return fulfillJson(route, { title: 'PurchaseOrder.ReceiptQuantityOverRemaining' }, 422);
  }

  const lines = applyReceiptLines(order, payload);
  const status = lines.every((line) => (line.remainingQuantity ?? line.expectedQuantity) === 0)
    ? 'Received'
    : 'PartiallyReceived';
  const receipt = createReceipt(order, payload);
  const updatedOrder: PurchaseOrderDetail = {
    ...order,
    status,
    receivedQuantity: lines.reduce((total, line) => total + (line.receivedQuantity ?? 0), 0),
    lines,
    receipts: [...(order.receipts ?? []), receipt],
  };

  state.details.set(updatedOrder.purchaseOrderId, updatedOrder);
  state.orders = state.orders.map((item) =>
    item.purchaseOrderId === updatedOrder.purchaseOrderId
      ? { ...item, status, receivedQuantity: updatedOrder.receivedQuantity }
      : item,
  );
  return fulfillJson(route, updatedOrder);
}

function canReceive(order: PurchaseOrderDetail, payload: ReceivePurchaseOrderRequest): boolean {
  return (
    payload.lines.length > 0 &&
    payload.lines.every((receiptLine) => {
      const line = order.lines.find((item) => item.lineId === receiptLine.purchaseOrderLineId);
      const remaining = line?.remainingQuantity ?? line?.expectedQuantity ?? 0;
      return receiptLine.quantity > 0 && receiptLine.quantity <= remaining;
    })
  );
}

function applyReceiptLines(order: PurchaseOrderDetail, payload: ReceivePurchaseOrderRequest) {
  return order.lines.map((line) => {
    const received = payload.lines.find((item) => item.purchaseOrderLineId === line.lineId);
    if (!received) return line;

    const remaining = line.remainingQuantity ?? line.expectedQuantity;
    return {
      ...line,
      receivedQuantity: (line.receivedQuantity ?? 0) + received.quantity,
      remainingQuantity: remaining - received.quantity,
    };
  });
}

function createReceipt(order: PurchaseOrderDetail, payload: ReceivePurchaseOrderRequest) {
  const receiptNumber = `REC-2026-${String((order.receipts?.length ?? 0) + 1).padStart(3, '0')}`;
  return {
    receiptId: `receipt-${(order.receipts?.length ?? 0) + 1}`,
    receiptNumber,
    receivedAt: '2026-07-29T10:30:00.000Z',
    referenceNumber: payload.referenceNumber,
    notes: payload.notes,
    receivedByUserId: 'user-owner',
    receivedByDisplayName: 'Owner Auditor',
    lines: payload.lines.map((line, index) => ({
      receiptLineId: `${receiptNumber}-line-${index + 1}`,
      purchaseOrderLineId: line.purchaseOrderLineId,
      itemId: order.lines.find((item) => item.lineId === line.purchaseOrderLineId)?.itemId ?? '',
      inventoryBatchId: `batch-${receiptNumber}-${index + 1}`,
      batchNumber: line.batchNumber,
      batchVoided: false,
      stockTransactionId: `transaction-${receiptNumber}-${index + 1}`,
      quantity: line.quantity,
      totalPurchaseCost: line.totalPurchaseCost,
      unitCost: line.quantity ? line.totalPurchaseCost / line.quantity : 0,
      mrp: line.mrp,
      salesPrice: line.salesPrice,
      taxRatePercent: line.taxRatePercent,
      taxIncluded: line.taxIncluded,
      purchaseTaxIncluded: line.purchaseTaxIncluded,
    })),
  };
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

function toDetail(
  order: PurchaseOrderListItem,
  opts: {
    withLongLines?: boolean;
    withReceiptHistory?: boolean;
    withLongSupplierDetails?: boolean;
    withLongNotes?: boolean;
    withDenseLines?: boolean;
  } = {},
): PurchaseOrderDetail {
  const lines = opts.withDenseLines ? denseLinesPurchaseOrder() : opts.withLongLines ? longPurchaseOrderLines() : [];
  const supplierName = opts.withLongSupplierDetails
    ? 'Very Long International Supplier Name With Deterministic Audit Value'
    : order.supplierName;
  const supplierReference = opts.withLongSupplierDetails
    ? 'SUPPLIER-REFERENCE-WITH-A-LONG-DETERMINISTIC-VALUE-2026-000001'
    : order.supplierReference;
  const notes = opts.withLongNotes
    ? Array.from({ length: 350 }, () => 'Deterministic long notes for A4 pagination coverage.').join(' ')
    : null;

  const detail: PurchaseOrderDetail = {
    purchaseOrderId: order.purchaseOrderId,
    purchaseOrderNumber: order.purchaseOrderNumber,
    status: order.status,
    supplierId: null,
    supplierName,
    supplierReference,
    receivedQuantity: order.receivedQuantity,
    orderDate: orderDate(order),
    expectedDeliveryDate: null,
    supplierReferenceNumber: supplierReference,
    notes,
    lines,
    expectedTotal: lines.length > 0 ? lines.reduce((sum, line) => sum + line.lineTotal, 0) : order.expectedTotal,
    createdAt: order.createdAt,
    cancellationReason: null,
  };

  if (opts.withReceiptHistory) {
    detail.receipts = purchaseOrderReceiptHistory();
  }

  return detail;
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
