import type { Item } from '../services/inventory.models';
import { InventoryActions } from './inventory.actions';
import { InventoryState, inventoryReducer } from './inventory.reducer';

const milkItem: Item = {
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

describe('inventoryReducer', () => {
  const initialState = inventoryReducer(undefined, { type: '@@INIT' } as never);

  it('sets loading state and latest query when load items is requested', () => {
    const query = {
      search: 'milk',
      status: 'active' as const,
      pageNumber: 2,
      pageSize: 10,
    };

    const next = inventoryReducer(
      {
        ...initialState,
        errorMessage: 'Existing error',
      },
      InventoryActions.loadItemsRequested({ query })
    );

    expect(next.loadingItems).toBe(true);
    expect(next.errorMessage).toBe('');
    expect(next.latestQuery).toEqual(query);
  });

  it('sets catalog metadata and summary when load items succeeds', () => {
    const next = inventoryReducer(
      {
        ...initialState,
        loadingItems: true,
      },
      InventoryActions.loadItemsSucceeded({
        items: [milkItem],
        totalCount: 12,
        pageNumber: 2,
        pageSize: 10,
        summary: {
          totalItems: 12,
          activeItems: 11,
          inactiveItems: 1,
          runningLowStockCount: 3,
          criticalStockCount: 1,
          totalStockValue: 4500,
        },
      })
    );

    expect(next.loadingItems).toBe(false);
    expect(next.ids).toEqual(['item-1']);
    expect(next.entities['item-1']).toEqual(milkItem);
    expect(next.totalCount).toBe(12);
    expect(next.pageNumber).toBe(2);
    expect(next.pageSize).toBe(10);
    expect(next.summary).toEqual({
      totalItems: 12,
      activeItems: 11,
      inactiveItems: 1,
      runningLowStockCount: 3,
      criticalStockCount: 1,
      totalStockValue: 4500,
    });
  });

  it('keeps a mutation error separate from a catalog-load error', () => {
    const mutationFailure = inventoryReducer(
      initialState,
      InventoryActions.addItemFailed({ errorMessage: 'errors.items.unableToAddItem' })
    );
    const loadFailure = inventoryReducer(
      mutationFailure,
      InventoryActions.loadItemsFailed({ errorMessage: 'errors.items.unableToLoadItems' })
    );

    expect(mutationFailure.loadErrorMessage).toBe('');
    expect(loadFailure.loadErrorMessage).toBe('errors.items.unableToLoadItems');
    expect(loadFailure.errorMessage).toBe('errors.items.unableToLoadItems');
  });

  it('keeps items unchanged when add item succeeds', () => {
    const state: InventoryState = {
      ...initialState,
      ids: ['item-1'],
      entities: { 'item-1': milkItem },
      submitting: true,
      lastMutationType: 'add-item',
      lastMutationSucceeded: false,
    };

    const next = inventoryReducer(
      state,
      InventoryActions.addItemSucceeded({
        item: {
          ...milkItem,
          id: 'item-2',
        },
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.ids).toEqual(['item-1']);
    expect(next.entities['item-1']).toEqual(milkItem);
    expect(next.entities['item-2']).toBeUndefined();
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.lastAddedItem).toEqual({
      ...milkItem,
      id: 'item-2',
    });
  });

  it('sets failure state when add item fails', () => {
    const state: InventoryState = {
      ...initialState,
      submitting: true,
      lastMutationType: 'add-item',
      lastMutationSucceeded: false,
    };

    const next = inventoryReducer(
      state,
      InventoryActions.addItemFailed({
        errorMessage: 'errors.items.unableToAddItem',
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('errors.items.unableToAddItem');
    expect(next.lastMutationType).toBe('add-item');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('keeps items unchanged when update item succeeds', () => {
    const state: InventoryState = {
      ...initialState,
      ids: ['item-1'],
      entities: { 'item-1': milkItem },
      submitting: true,
      lastMutationType: 'update-item',
      lastMutationSucceeded: false,
    };

    const next = inventoryReducer(state, InventoryActions.updateItemSucceeded());

    expect(next.submitting).toBe(false);
    expect(next.ids).toEqual(['item-1']);
    expect(next.entities['item-1']).toEqual(milkItem);
    expect(next.lastMutationSucceeded).toBe(true);
  });
});
