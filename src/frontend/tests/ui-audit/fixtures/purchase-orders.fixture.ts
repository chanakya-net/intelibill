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
}

export interface PurchaseOrdersScenarioOptions extends ShellScenarioOptions {
  readonly orders?: readonly PurchaseOrderListItem[];
  readonly apiState?: PurchaseOrdersApiState;
}

export const PURCHASE_ORDER_STATUSES: readonly PurchaseOrderListItem[] = [
  order('po-draft', 'Draft'),
  order('po-placed', 'Placed'),
  order('po-partial', 'PartiallyReceived', { receivedQuantity: 3 }),
  order('po-received', 'Received', { receivedQuantity: 8 }),
  order('po-closed', 'Closed', { receivedQuantity: 5 }),
  order('po-cancelled', 'Cancelled'),
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
  };
}

export async function installPurchaseOrdersFixture(
  page: Page,
  scenario: PurchaseOrdersScenario,
): Promise<void> {
  await installShellFixture(page, scenario.shell);
  await page.route(`${API_BASE}/purchase-orders**`, (route) =>
    handlePurchaseOrderRoute(route, scenario),
  );
}

function handlePurchaseOrderRoute(route: Route, scenario: PurchaseOrdersScenario): Promise<void> {
  if (scenario.apiState === 'loading') {
    return new Promise(() => undefined);
  }
  if (scenario.apiState === 'error') {
    return fulfillJson(route, { title: 'PurchaseOrder.LoadFailed' }, 503);
  }

  const url = new URL(route.request().url());
  const segments = url.pathname.split('/').filter(Boolean);
  if (segments.length > 2) {
    const order = scenario.orders.find((item) => item.purchaseOrderId === segments.at(-1));
    return fulfillJson(
      route,
      order ? toDetail(order) : { title: 'PurchaseOrder.NotFound' },
      order ? 200 : 404,
    );
  }

  return fulfillJson(route, listResult(scenario.orders, url.searchParams));
}

function listResult(orders: readonly PurchaseOrderListItem[], params: URLSearchParams) {
  const status = params.get('status') ?? '';
  const search = (params.get('search') ?? '').toLocaleLowerCase();
  const page = Number(params.get('page') ?? '1');
  const pageSize = Number(params.get('page_size') ?? '20');
  const matching = orders.filter(
    (order) =>
      (!status || order.status === status) && (!search || searchableText(order).includes(search)),
  );
  const start = (page - 1) * pageSize;

  return {
    items: matching.slice(start, start + pageSize),
    totalCount: matching.length,
    pageNumber: page,
    pageSize,
  };
}

function searchableText(order: PurchaseOrderListItem): string {
  return [order.purchaseOrderNumber, order.supplierName, order.supplierReference]
    .filter((value): value is string => !!value)
    .join(' ')
    .toLocaleLowerCase();
}

function toDetail(order: PurchaseOrderListItem): PurchaseOrderDetail {
  return {
    purchaseOrderId: order.purchaseOrderId,
    purchaseOrderNumber: order.purchaseOrderNumber,
    status: order.status,
    supplierId: null,
    supplierName: order.supplierName,
    supplierReference: order.supplierReference,
    receivedQuantity: order.receivedQuantity,
    orderDate: '2026-07-01',
    expectedDeliveryDate: null,
    supplierReferenceNumber: order.supplierReference,
    notes: null,
    lines: [],
    expectedTotal: order.expectedTotal,
    createdAt: order.createdAt,
    cancellationReason: null,
  };
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
