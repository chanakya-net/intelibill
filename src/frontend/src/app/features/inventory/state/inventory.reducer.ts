import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { Item } from '../services/inventory.service';
import { InventoryActions, ItemMutationType } from './inventory.actions';

export const inventoryFeatureKey = 'inventory';

export const inventoryAdapter = createEntityAdapter<Item>({
  selectId: (item) => item.itemId,
});

export interface InventoryState extends EntityState<Item> {
  readonly loadingItems: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: ItemMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: InventoryState = inventoryAdapter.getInitialState({
  loadingItems: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
});

export const inventoryReducer = createReducer(
  initialState,
  on(InventoryActions.loadItemsRequested, (state) => ({
    ...state,
    loadingItems: true,
    errorMessage: '',
  })),
  on(InventoryActions.loadItemsSucceeded, (state, { items }) =>
    inventoryAdapter.setAll([...items], {
      ...state,
      loadingItems: false,
      errorMessage: '',
    })
  ),
  on(InventoryActions.loadItemsFailed, (state, { errorMessage }) => ({
    ...state,
    loadingItems: false,
    errorMessage,
  })),

  on(InventoryActions.addItemRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'add-item',
    lastMutationSucceeded: false,
  })),
  on(InventoryActions.addItemSucceeded, (state, { item }) =>
    inventoryAdapter.addOne(item, {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationType: 'add-item',
      lastMutationSucceeded: true,
    })
  ),
  on(InventoryActions.addItemFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'add-item',
    lastMutationSucceeded: false,
  })),

  on(InventoryActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(InventoryActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  }))
);

export const inventoryFeature = createFeature({
  name: inventoryFeatureKey,
  reducer: inventoryReducer,
});
