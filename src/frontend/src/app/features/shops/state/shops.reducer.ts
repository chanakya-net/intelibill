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
  /** The detail read in flight. The effect keeps at most one, so a newer request replaces it. */
  readonly detailsRead: { readonly shopId: string; readonly overtaken: boolean } | null;
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
  detailsRead: null,
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

  on(ShopsActions.loadShopDetailsRequested, (state, { shopId }) => ({
    ...state,
    loadingDetails: true,
    errorMessage: '',
    detailsRead: { shopId, overtaken: false },
  })),
  on(ShopsActions.loadShopDetailsSucceeded, (state, { details }) => isOvertakenRead(state, details.shopId)
    ? { ...state, loadingDetails: false, detailsRead: null }
    : {
        ...state,
        loadingDetails: false,
        errorMessage: '',
        detailsRead: state.detailsRead?.shopId === details.shopId ? null : state.detailsRead,
        detailsById: {
          ...state.detailsById,
          [details.shopId]: details,
        },
      }),
  on(ShopsActions.loadShopDetailsFailed, (state, { errorMessage, shopId }) => ({
    ...state,
    loadingDetails: false,
    errorMessage: shopId && isOvertakenRead(state, shopId) ? state.errorMessage : errorMessage,
    detailsRead: !shopId || state.detailsRead?.shopId === shopId ? null : state.detailsRead,
    detailsById: shopId ? withoutDetails(state.detailsById, shopId) : state.detailsById,
  })),
  on(ShopsActions.shopLicenceRefused, (state, { shopId, securitySuspended, errorMessage }) => ({
    ...state,
    ...afterLicenceRefusal(state, shopId, true, securitySuspended),
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
  on(ShopsActions.updateShopFailed, (state, { errorMessage, shopId, licenceRefused, securitySuspended }) => ({
    ...state,
    submitting: false,
    errorMessage,
    ...afterLicenceRefusal(state, shopId, licenceRefused, securitySuspended),
    lastMutationType: 'update',
    lastMutationSucceeded: false,
  })),

  on(ShopsActions.switchActiveShopRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'switch' as const,
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.switchActiveShopSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'switch' as const,
    lastMutationSucceeded: true,
  })),
  on(ShopsActions.switchActiveShopFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'switch' as const,
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

  on(ShopsActions.updateShopBankDetailsRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-bank-details' as const,
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.updateShopBankDetailsSucceeded, (state, { details }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'update-bank-details' as const,
    lastMutationSucceeded: true,
    detailsById: {
      ...state.detailsById,
      [details.shopId]: details,
    },
  })),
  on(ShopsActions.updateShopBankDetailsFailed, (state, { errorMessage, shopId, licenceRefused, securitySuspended }) => ({
    ...state,
    submitting: false,
    errorMessage,
    ...afterLicenceRefusal(state, shopId, licenceRefused, securitySuspended),
    lastMutationType: 'update-bank-details' as const,
    lastMutationSucceeded: false,
  })),

  on(ShopsActions.updateShopTaxProfileRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-tax-profile' as const,
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.updateShopTaxProfileSucceeded, (state, { details }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'update-tax-profile' as const,
    lastMutationSucceeded: true,
    detailsById: {
      ...state.detailsById,
      [details.shopId]: details,
    },
  })),
  on(ShopsActions.updateShopTaxProfileFailed, (state, { errorMessage, shopId, licenceRefused, securitySuspended }) => ({
    ...state,
    submitting: false,
    errorMessage,
    ...afterLicenceRefusal(state, shopId, licenceRefused, securitySuspended),
    lastMutationType: 'update-tax-profile' as const,
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


function withoutDetails(details: ShopsState['detailsById'], shopId: string): ShopsState['detailsById'] {
  if (!details[shopId]) return details;
  const remaining = { ...details };
  delete remaining[shopId];
  return remaining;
}

function isOvertakenRead(state: ShopsState, shopId: string): boolean {
  return state.detailsRead?.shopId === shopId && state.detailsRead.overtaken;
}

/** Drops what the refusal outdated, including whatever a read already in flight would bring back. */
function afterLicenceRefusal(state: ShopsState, shopId?: string, refused?: boolean,
  security?: boolean): Pick<ShopsState, 'detailsById' | 'detailsRead'> {
  const { detailsById, detailsRead } = state;
  if (!shopId || !refused) return { detailsById, detailsRead };
  return {
    detailsById: invalidateSetupAccess(detailsById, shopId, security),
    detailsRead: detailsRead?.shopId === shopId ? { shopId, overtaken: true } : detailsRead,
  };
}

function invalidateSetupAccess(details: ShopsState['detailsById'], shopId: string,
  security?: boolean): ShopsState['detailsById'] {
  if (!details[shopId]) return details;
  if (security) return withoutDetails(details, shopId);
  return { ...details, [shopId]: { ...details[shopId], setupAccess: null } };
}
