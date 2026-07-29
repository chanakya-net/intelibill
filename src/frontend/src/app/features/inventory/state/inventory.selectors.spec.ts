import type { Item } from '../services/inventory.models';
import { InventoryState } from './inventory.reducer';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastAddedItem,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadErrorMessage,
  selectInventoryLatestQuery,
  selectInventoryLoadingItems,
  selectInventoryPagination,
  selectInventorySubmitting,
  selectInventorySummary,
} from './inventory.selectors';

const itemOne: Item = {
  id: 'item-1',
  name: 'Milk',
  barcode: 'B001',
  description: null,
  uom: 'ltr',
  isActive: true,
  currentStock: 10,
  unitPrice: 45,
  currentStockValue: 450,
  reorderLevel: 5,
  stockStatus: 'inStock',
  hsnCode: '0401',
  defaultTaxRatePercent: 5,
  defaultTaxIncluded: false,
};

const itemTwo: Item = {
  id: 'item-2',
  name: 'Bread',
  barcode: 'B002',
  description: null,
  uom: 'pcs',
  isActive: true,
  currentStock: 4,
  unitPrice: 30,
  currentStockValue: 120,
  reorderLevel: 10,
  stockStatus: 'runningLow',
  hsnCode: null,
  defaultTaxRatePercent: 0,
  defaultTaxIncluded: false,
};

describe('inventory selectors', () => {
  const inventoryState: InventoryState = {
    ids: ['item-1', 'item-2'],
    entities: {
      'item-1': itemOne,
      'item-2': itemTwo,
    },
    loadingItems: true,
    submitting: true,
    errorMessage: 'errors.items.unableToLoadItems',
    loadErrorMessage: 'errors.items.unableToLoadItems',
    lastMutationType: 'add-item',
    lastMutationSucceeded: true,
    lastAddedItem: itemTwo,
    totalCount: 2,
    pageNumber: 3,
    pageSize: 25,
    summary: {
      totalItems: 2,
      activeItems: 2,
      inactiveItems: 0,
      runningLowStockCount: 1,
      criticalStockCount: 0,
      totalStockValue: 570,
    },
    latestQuery: {
      search: 'milk',
      status: 'active',
      pageNumber: 3,
      pageSize: 25,
    },
  };

  const rootState = {
    inventory: inventoryState,
  };

  it('selects inventory items list', () => {
    expect(selectInventoryItems(rootState as never)).toEqual([itemOne, itemTwo]);
  });

  it('selects loading items state', () => {
    expect(selectInventoryLoadingItems(rootState as never)).toBe(true);
  });

  it('selects submitting state', () => {
    expect(selectInventorySubmitting(rootState as never)).toBe(true);
  });

  it('selects error message', () => {
    expect(selectInventoryErrorMessage(rootState as never)).toBe('errors.items.unableToLoadItems');
  });

  it('selects the catalog-load error separately from mutation errors', () => {
    expect(selectInventoryLoadErrorMessage(rootState as never)).toBe('errors.items.unableToLoadItems');
  });

  it('selects last mutation type', () => {
    expect(selectInventoryLastMutationType(rootState as never)).toBe('add-item');
  });

  it('selects last mutation status', () => {
    expect(selectInventoryLastMutationSucceeded(rootState as never)).toBe(true);
  });

  it('selects last added item', () => {
    expect(selectInventoryLastAddedItem(rootState as never)).toEqual(itemTwo);
  });

  it('selects catalog pagination', () => {
    expect(selectInventoryPagination(rootState as never)).toEqual({
      totalCount: 2,
      pageNumber: 3,
      pageSize: 25,
    });
  });

  it('selects catalog summary', () => {
    expect(selectInventorySummary(rootState as never)).toEqual(inventoryState.summary);
  });

  it('selects latest query metadata', () => {
    expect(selectInventoryLatestQuery(rootState as never)).toEqual(inventoryState.latestQuery);
  });
});
