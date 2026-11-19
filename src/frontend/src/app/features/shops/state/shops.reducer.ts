import { createFeature, createReducer, on } from '@ngrx/store';

import { UserShop } from '../../../core/auth/auth.models';
import { ShopDetails } from '../services/shop.service';
import { ShopMutationType, ShopsActions } from './shops.actions';

export const shopsFeatureKey = 'shops';

interface DetailRequest {
  readonly shopId: string;
  readonly overtaken: boolean;
}

export interface ShopsState {
  readonly shops: readonly UserShop[];
  readonly selectedShopId: string | null;
  readonly detailsById: Readonly<Record<string, ShopDetails>>;
  readonly loadingShops: boolean;
  readonly loadingDetails: boolean;
  /** The detail read in flight. The effect keeps at most one, so a newer request replaces it. */
  readonly detailsRead: DetailRequest | null;
  /**
   * The detail-bearing saves in flight, at most one of each kind. A licence refusal for the shop
   * marks them overtaken, so a save that the server finished before the refusal cannot bring that
   * shop's details or permissions back when its answer finally arrives.
   */
  readonly detailWrites: Readonly<Partial<Record<ShopMutationType, DetailRequest>>>;
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
  detailWrites: {},
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
  on(ShopsActions.loadShopDetailsFailed, (state, { errorMessage, shopId, licenceRefused }) => ({
    ...state,
    loadingDetails: false,
    errorMessage: shopId && isOvertakenRead(state, shopId) ? state.errorMessage : errorMessage,
    detailsRead: !shopId || state.detailsRead?.shopId === shopId ? null : state.detailsRead,
    detailWrites: shopId && licenceRefused ? overtakenWrites(state.detailWrites, shopId) : state.detailWrites,
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

  on(ShopsActions.updateShopRequested, (state, { shopId }) => ({
    ...state,
    detailWrites: { ...state.detailWrites, update: { shopId, overtaken: false } },
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update',
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.updateShopSucceeded, (state, { details }) => ({
    ...state,
    submitting: false,
    lastMutationType: 'update',
    lastMutationSucceeded: true,
    ...afterSave(state, 'update', details),
  })),
  on(ShopsActions.updateShopFailed, (state, { errorMessage, shopId, licenceRefused, securitySuspended }) => ({
    ...state,
    submitting: false,
    errorMessage,
    ...afterLicenceRefusal(state, shopId, licenceRefused, securitySuspended, 'update'),
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

  on(ShopsActions.updateShopBankDetailsRequested, (state, { shopId }) => ({
    ...state,
    detailWrites: { ...state.detailWrites, 'update-bank-details': { shopId, overtaken: false } },
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-bank-details' as const,
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.updateShopBankDetailsSucceeded, (state, { details }) => ({
    ...state,
    submitting: false,
    lastMutationType: 'update-bank-details' as const,
    lastMutationSucceeded: true,
    ...afterSave(state, 'update-bank-details', details),
  })),
  on(ShopsActions.updateShopBankDetailsFailed, (state, { errorMessage, shopId, licenceRefused, securitySuspended }) => ({
    ...state,
    submitting: false,
    errorMessage,
    ...afterLicenceRefusal(state, shopId, licenceRefused, securitySuspended, 'update-bank-details'),
    lastMutationType: 'update-bank-details' as const,
    lastMutationSucceeded: false,
  })),

  on(ShopsActions.updateShopTaxProfileRequested, (state, { shopId }) => ({
    ...state,
    detailWrites: { ...state.detailWrites, 'update-tax-profile': { shopId, overtaken: false } },
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-tax-profile' as const,
    lastMutationSucceeded: false,
  })),
  on(ShopsActions.updateShopTaxProfileSucceeded, (state, { details }) => ({
    ...state,
    submitting: false,
    lastMutationType: 'update-tax-profile' as const,
    lastMutationSucceeded: true,
    ...afterSave(state, 'update-tax-profile', details),
  })),
  on(ShopsActions.updateShopTaxProfileFailed, (state, { errorMessage, shopId, licenceRefused, securitySuspended }) => ({
    ...state,
    submitting: false,
    errorMessage,
    ...afterLicenceRefusal(state, shopId, licenceRefused, securitySuspended, 'update-tax-profile'),
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

/**
 * Drops what the refusal outdated, including whatever a read or save already in flight would
 * bring back. `finished` is the save that has just answered, when the refusal is its own.
 */
function afterLicenceRefusal(state: ShopsState, shopId?: string, refused?: boolean, security?: boolean,
  finished?: ShopMutationType): Pick<ShopsState, 'detailsById' | 'detailsRead' | 'detailWrites'> {
  const { detailsById, detailsRead } = state;
  const detailWrites = withoutWrite(state.detailWrites, finished);
  if (!shopId || !refused) return { detailsById, detailsRead, detailWrites };
  return {
    detailsById: invalidateSetupAccess(detailsById, shopId, security),
    detailsRead: detailsRead?.shopId === shopId ? { shopId, overtaken: true } : detailsRead,
    detailWrites: overtakenWrites(detailWrites, shopId),
  };
}

/** A save completes either way. Only one that no refusal overtook publishes what it returned. */
function afterSave(state: ShopsState, type: ShopMutationType,
  details: ShopDetails): Pick<ShopsState, 'detailsById' | 'detailWrites' | 'errorMessage'> {
  const write = state.detailWrites[type];
  const detailWrites = withoutWrite(state.detailWrites, type);
  if (write?.shopId === details.shopId && write.overtaken) {
    return { detailsById: state.detailsById, detailWrites, errorMessage: state.errorMessage };
  }
  return { detailsById: { ...state.detailsById, [details.shopId]: details }, detailWrites, errorMessage: '' };
}

function withoutWrite(writes: ShopsState['detailWrites'], type?: ShopMutationType): ShopsState['detailWrites'] {
  if (!type || !writes[type]) return writes;
  const remaining = { ...writes };
  delete remaining[type];
  return remaining;
}

function overtakenWrites(writes: ShopsState['detailWrites'], shopId: string): ShopsState['detailWrites'] {
  return Object.fromEntries(Object.entries(writes).map(([type, write]) =>
    [type, write.shopId === shopId ? { shopId, overtaken: true } : write]));
}

function invalidateSetupAccess(details: ShopsState['detailsById'], shopId: string,
  security?: boolean): ShopsState['detailsById'] {
  if (!details[shopId]) return details;
  if (security) return withoutDetails(details, shopId);
  return { ...details, [shopId]: { ...details[shopId], setupAccess: null } };
}
