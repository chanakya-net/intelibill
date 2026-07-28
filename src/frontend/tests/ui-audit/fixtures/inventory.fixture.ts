import type { Page, Route } from '@playwright/test';

import type {
  InventoryCatalogResponse,
  Item,
  ItemCatalogStatusFilter,
} from '../../../src/app/features/inventory/services/inventory.models';
import {
  createShellScenario,
  installShellFixture,
  type ShellScenario,
  type ShellScenarioOptions,
} from './shell.fixture';

const API_BASE = 'http://localhost:5277/api';

export type InventoryApiState = 'ready' | 'loading' | 'error';

export interface InventoryScenario {
  readonly shell: ShellScenario;
  readonly items: readonly Item[];
  readonly apiState: InventoryApiState;
  readonly mutationState: 'ready' | 'error';
}

export interface InventoryScenarioOptions extends ShellScenarioOptions {
  readonly items?: readonly Item[];
  readonly apiState?: InventoryApiState;
  readonly mutationState?: InventoryScenario['mutationState'];
}

export const LONG_INVENTORY_ITEM = item('long-item', {
  name: 'Extraordinarily long inventory item name that remains readable in compact product cards',
  barcode: 'LONG-BARCODE-1234567890123456789012345678901234567890',
  description: 'A deterministic long product description used to exercise bounded table cells.',
});

export const INVENTORY_ITEMS = [
  item('milk', { name: 'Fresh milk', barcode: 'MILK-001', currentStock: 8 }),
  item('critical', {
    name: 'Critical stock product',
    barcode: 'CRITICAL-002',
    currentStock: 0,
    stockStatus: 'critical',
  }),
  item('inactive', {
    name: 'Inactive product',
    barcode: 'INACTIVE-003',
    isActive: false,
    stockStatus: 'inactive',
  }),
  LONG_INVENTORY_ITEM,
];

export const DENSE_INVENTORY_ITEMS = Array.from({ length: 31 }, (_, index) =>
  index === 0
    ? LONG_INVENTORY_ITEM
    : item(`dense-${index + 1}`, {
        name: `Dense inventory product ${index + 1}`,
        barcode: `DENSE-${String(index + 1).padStart(4, '0')}`,
      }),
);

export function createInventoryScenario(options: InventoryScenarioOptions = {}): InventoryScenario {
  return {
    shell: createShellScenario(options),
    items: options.items ?? INVENTORY_ITEMS,
    apiState: options.apiState ?? 'ready',
    mutationState: options.mutationState ?? 'ready',
  };
}

export async function installInventoryFixture(
  page: Page,
  scenario: InventoryScenario,
): Promise<void> {
  await installShellFixture(page, scenario.shell);
  const state: InventoryFixtureState = {
    items: [...scenario.items],
    apiState: scenario.apiState,
    mutationState: scenario.mutationState,
  };

  await page.route(`${API_BASE}/items**`, (route) => handleItemsRoute(route, state));
  await page.route(`${API_BASE}/hsn/lookup`, (route) =>
    fulfillJson(route, {
      hsnCodes: ['0401'],
      taxScenarios: [{ condition: 'Audit rate', taxPercentage: '5%' }],
    }),
  );
}

interface InventoryFixtureState {
  items: Item[];
  readonly apiState: InventoryApiState;
  readonly mutationState: InventoryScenario['mutationState'];
}

async function handleItemsRoute(route: Route, state: InventoryFixtureState): Promise<void> {
  const request = route.request();
  const url = new URL(request.url());

  if (request.method() === 'GET' && url.pathname === '/api/items/stream') {
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body: '' });
    return;
  }

  if (request.method() === 'GET' && url.pathname === '/api/items') {
    if (state.apiState === 'loading') {
      await new Promise<void>(() => undefined);
      return;
    }
    if (state.apiState === 'error') {
      await fulfillJson(route, { title: 'Inventory.LoadFailed' }, 503);
      return;
    }
    await fulfillJson(route, listItems(state.items, url.searchParams));
    return;
  }

  if (request.method() === 'POST' && url.pathname === '/api/items/barcodes/generate') {
    await fulfillJson(route, { barcode: 'AUDIT-GENERATED-001' });
    return;
  }

  if (request.method() === 'POST' && url.pathname === '/api/items') {
    await addItem(route, state);
    return;
  }

  if (request.method() === 'PATCH') {
    await updateItem(route, state);
    return;
  }

  await route.abort('blockedbyclient');
}

function listItems(items: readonly Item[], parameters: URLSearchParams): InventoryCatalogResponse {
  const search = (parameters.get('search') ?? '').toLocaleLowerCase();
  const status = (parameters.get('status') ?? 'all') as ItemCatalogStatusFilter;
  const pageNumber = Number(parameters.get('pageNumber') ?? '1');
  const pageSize = Number(parameters.get('pageSize') ?? '20');
  const matching = items.filter((candidate) => matches(candidate, search, status));
  const start = (pageNumber - 1) * pageSize;

  return {
    items: matching.slice(start, start + pageSize),
    totalCount: matching.length,
    pageNumber,
    pageSize,
    summary: summaryFor(items),
  };
}

function matches(item: Item, search: string, status: ItemCatalogStatusFilter): boolean {
  const hasSearchMatch =
    !search || `${item.name} ${item.barcode}`.toLocaleLowerCase().includes(search);
  if (!hasSearchMatch || status === 'all') return hasSearchMatch;
  if (status === 'active') return item.isActive;
  if (status === 'inactive') return !item.isActive;
  return item.stockStatus === status;
}

function summaryFor(items: readonly Item[]) {
  return {
    totalItems: items.length,
    activeItems: items.filter((item) => item.isActive).length,
    inactiveItems: items.filter((item) => !item.isActive).length,
    runningLowStockCount: items.filter((item) => item.stockStatus === 'runningLow').length,
    criticalStockCount: items.filter((item) => item.stockStatus === 'critical').length,
    totalStockValue: items.reduce((total, item) => total + item.currentStockValue, 0),
  };
}

async function addItem(route: Route, state: InventoryFixtureState): Promise<void> {
  if (state.mutationState === 'error') {
    await fulfillJson(route, { title: 'Item.BarcodeAlreadyExists' }, 409);
    return;
  }

  const payload = route.request().postDataJSON() as Pick<
    Item,
    'name' | 'barcode' | 'description' | 'uom' | 'isActive' | 'hsnCode' | 'defaultTaxRatePercent'
  >;
  const created = item(`created-${state.items.length + 1}`, {
    ...payload,
    currentStock: 0,
    currentStockValue: 0,
    unitPrice: null,
    stockStatus: 'critical',
  });
  state.items = [...state.items, created];
  await fulfillJson(route, created);
}

async function updateItem(route: Route, state: InventoryFixtureState): Promise<void> {
  if (state.mutationState === 'error') {
    await fulfillJson(route, { title: 'Item.BarcodeAlreadyExists' }, 409);
    return;
  }

  const itemId = new URL(route.request().url()).pathname.split('/').at(-1);
  const payload = route.request().postDataJSON() as Partial<Item>;
  state.items = state.items.map((item) => (item.id === itemId ? { ...item, ...payload } : item));
  await route.fulfill({ status: 204 });
}

function item(id: string, overrides: Partial<Item> = {}): Item {
  return {
    id,
    name: `Audit item ${id}`,
    barcode: `AUDIT-${id.toUpperCase()}`,
    description: null,
    uom: 'PCS',
    isActive: true,
    currentStock: 25,
    unitPrice: 120,
    currentStockValue: 3000,
    reorderLevel: 10,
    stockStatus: 'inStock',
    hsnCode: '0401',
    defaultTaxRatePercent: 5,
    defaultTaxIncluded: false,
    ...overrides,
  };
}

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}
