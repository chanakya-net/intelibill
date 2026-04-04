import { InventoryActions } from './inventory.actions';
import { InventoryState, inventoryReducer } from './inventory.reducer';

describe('inventoryReducer', () => {
  const initialState = inventoryReducer(undefined, { type: '@@INIT' } as never);

  it('sets loading state when load items is requested', () => {
    const next = inventoryReducer(
      {
        ...initialState,
        errorMessage: 'Existing error',
      },
      InventoryActions.loadItemsRequested()
    );

    expect(next.loadingItems).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets items when load items succeeds', () => {
    const next = inventoryReducer(
      {
        ...initialState,
        loadingItems: true,
      },
      InventoryActions.loadItemsSucceeded({
        items: [
          {
            itemId: 'item-1',
            name: 'Milk',
            barcode: 'B001',
            description: null,
            uom: 'ltr',
            isActive: true,
            preferredSupplierId: null,
          },
        ],
      })
    );

    expect(next.loadingItems).toBe(false);
    expect(next.items).toHaveLength(1);
  });

  it('sets error when load items fails', () => {
    const next = inventoryReducer(
      {
        ...initialState,
        loadingItems: true,
      },
      InventoryActions.loadItemsFailed({ errorMessage: 'errors.items.unableToLoadItems' })
    );

    expect(next.loadingItems).toBe(false);
    expect(next.errorMessage).toBe('errors.items.unableToLoadItems');
  });

  it('sets submitting state when add item is requested', () => {
    const next = inventoryReducer(
      {
        ...initialState,
        errorMessage: 'Existing error',
      },
      InventoryActions.addItemRequested({
        payload: {
          name: 'Milk',
          barcode: 'B001',
          description: null,
          uom: 'ltr',
          isActive: true,
          preferredSupplierId: null,
        },
      })
    );

    expect(next.submitting).toBe(true);
    expect(next.errorMessage).toBe('');
    expect(next.lastMutationType).toBe('add-item');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('appends item when add item succeeds', () => {
    const state: InventoryState = {
      ...initialState,
      submitting: true,
      lastMutationType: 'add-item',
      lastMutationSucceeded: false,
    };

    const next = inventoryReducer(
      state,
      InventoryActions.addItemSucceeded({
        item: {
          itemId: 'item-1',
          name: 'Milk',
          barcode: 'B001',
          description: null,
          uom: 'ltr',
          isActive: true,
          preferredSupplierId: null,
        },
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.items).toHaveLength(1);
    expect(next.lastMutationSucceeded).toBe(true);
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
});
