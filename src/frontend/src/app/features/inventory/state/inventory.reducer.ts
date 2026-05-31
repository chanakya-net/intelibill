import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import type {
  InventoryCatalogQuery,
  InventoryCatalogSummary,
  Item,
} from '../services/inventory.models';
import { InventoryActions, ItemMutationType } from './inventory.actions';

export const inventoryFeatureKey = 'inventory';

export const inventoryAdapter = createEntityAdapter<Item>({
  selectId: (item) => item.id,
});

export interface InventoryState extends EntityState<Item> {
  readonly loadingItems: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: ItemMutationType | null;
  readonly lastMutationSucceeded: boolean;
  readonly lastAddedItem: Item | null;
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly summary: InventoryCatalogSummary | null;
  readonly latestQuery: InventoryCatalogQuery;
}

const initialState: InventoryState = inventoryAdapter.getInitialState({
  loadingItems: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  lastAddedItem: null,
  totalCount: 0,
  pageNumber: 1,
  pageSize: 20,
  summary: null,
  latestQuery: {
    search: '',
    status: 'all',
    pageNumber: 1,
    pageSize: 20,
  },
});

export const inventoryReducer = createReducer(
  initialState,
  on(InventoryActions.loadItemsRequested, (state, { query }) => ({
    ...state,
    loadingItems: true,
    errorMessage: '',
    latestQuery: query ?? state.latestQuery,
  })),
  on(InventoryActions.loadItemsSucceeded, (state, { items, totalCount, pageNumber, pageSize, summary }) =>
    inventoryAdapter.setAll([...items], {
      ...state,
      loadingItems: false,
      errorMessage: '',
      totalCount,
      pageNumber,
      pageSize,
      summary,
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
  on(InventoryActions.addItemSucceeded, (state, { item }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'add-item',
    lastMutationSucceeded: true,
    lastAddedItem: item,
  })),
  on(InventoryActions.addItemFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'add-item',
    lastMutationSucceeded: false,
  })),

  on(InventoryActions.updateItemRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-item',
    lastMutationSucceeded: false,
  })),
  on(InventoryActions.updateItemSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'update-item',
    lastMutationSucceeded: true,
  })),
  on(InventoryActions.updateItemFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'update-item',
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
    lastAddedItem: null,
  }))
);

export const inventoryFeature = createFeature({
  name: inventoryFeatureKey,
  reducer: inventoryReducer,
});
