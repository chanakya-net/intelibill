import { createFeature, createReducer, on } from '@ngrx/store';

import { UserShop } from '../../../core/auth/auth.models';
import { ShopDetails } from '../services/shop.service';
import { ShopMutationType, ShopsActions } from './shops.actions';

export const shopsFeatureKey = 'shops';

export interface ShopsState {
  readonly shops: readonly UserShop[];
  readonly selectedShopId: string | null;
  readonly detailsById: Readonly<Record<string, ShopDetails>>;
  readonly loadingShops: boolean;
  readonly loadingDetails: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: ShopMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: ShopsState = {
  shops: [],
  selectedShopId: null,
  detailsById: {},
  loadingShops: false,
  loadingDetails: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
};

export const shopsReducer = createReducer(
  initialState,
  on(ShopsActions.loadShopsRequested, (state) => ({
    ...state,
    loadingShops: true,
    errorMessage: '',
  })),
  on(ShopsActions.loadShopsSucceeded, (state, { shops }) => ({
    ...state,
    shops,
    loadingShops: false,
    errorMessage: '',
  })),
  on(ShopsActions.loadShopsFailed, (state, { errorMessage }) => ({
    ...state,
    loadingShops: false,
    errorMessage,
  })),

  on(ShopsActions.selectShop, (state, { shopId }) => ({
    ...state,
    selectedShopId: shopId,
  })),

  on(ShopsActions.loadShopDetailsRequested, (state) => ({
    ...state,
    loadingDetails: true,
    errorMessage: '',
  })),
  on(ShopsActions.loadShopDetailsSucceeded, (state, { details }) => ({
    ...state,
    loadingDetails: false,
    errorMessage: '',
    detailsById: {
      ...state.detailsById,
      [details.shopId]: details,
    },
  })),
  on(ShopsActions.loadShopDetailsFailed, (state, { errorMessage }) => ({
    ...state,
    loadingDetails: false,
    errorMessage,
  })),

  on(ShopsActions.createShopRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'create',
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.createShopSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'create',
    lastMutationSucceeded: true,
  })),
  on(ShopsActions.createShopFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'create',
    lastMutationSucceeded: false,
  })),

  on(ShopsActions.updateShopRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update',
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.updateShopSucceeded, (state, { details }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'update',
    lastMutationSucceeded: true,
    detailsById: {
      ...state.detailsById,
      [details.shopId]: details,
    },
  })),
  on(ShopsActions.updateShopFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'update',
    lastMutationSucceeded: false,
  })),

  on(ShopsActions.setDefaultShopRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'set-default',
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.setDefaultShopSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'set-default',
    lastMutationSucceeded: true,
  })),
  on(ShopsActions.setDefaultShopFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'set-default',
    lastMutationSucceeded: false,
  })),

  on(ShopsActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(ShopsActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  }))
);

export const shopsFeature = createFeature({
  name: shopsFeatureKey,
  reducer: shopsReducer,
});
