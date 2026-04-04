import { createFeature, createReducer, on } from '@ngrx/store';

import { Item } from '../services/inventory.service';
import { InventoryActions, ItemMutationType } from './inventory.actions';

export const inventoryFeatureKey = 'inventory';

export interface InventoryState {
  readonly items: readonly Item[];
  readonly loadingItems: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: ItemMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: InventoryState = {
  items: [],
  loadingItems: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
};

export const inventoryReducer = createReducer(
  initialState,
  on(InventoryActions.loadItemsRequested, (state) => ({
    ...state,
    loadingItems: true,
    errorMessage: '',
  })),
  on(InventoryActions.loadItemsSucceeded, (state, { items }) => ({
    ...state,
    loadingItems: false,
    items,
    errorMessage: '',
  })),
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
  on(InventoryActions.addItemSucceeded, (state, { item }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    items: [item, ...state.items],
    lastMutationType: 'add-item',
    lastMutationSucceeded: true,
  })),
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
