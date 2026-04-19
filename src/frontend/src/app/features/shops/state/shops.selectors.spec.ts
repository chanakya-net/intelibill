import { UserShop } from '../../../core/auth/auth.models';
import { ShopDetails } from '../services/shop.service';
import { ShopsState } from './shops.reducer';
import {
  selectSelectedShopDetails,
  selectSelectedShopId,
  selectShopDetailsEntities,
  selectShops,
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from './shops.selectors';

const baseState: ShopsState = {
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

const shopA: UserShop = { shopId: 'sh1', shopName: 'Shop A', isDefault: true, role: 'Owner', lastUsedAt: null };
const detailA: ShopDetails = {
  shopId: 'sh1', name: 'Shop A', address: '1 Main', city: 'C', state: 'S', pincode: '560001',
  contactPerson: null, mobileNumber: null, gstNumber: null,
  bankName: null, bankAccountNumber: null, bankAccountType: null, ifscCode: null, accountHolderName: null,
};

describe('shops selectors', () => {
  it('selectShops returns shops array', () => {
    const state = { ...baseState, shops: [shopA] };
    expect(selectShops.projector(state)).toEqual([shopA]);
  });

  it('selectSelectedShopId returns selected ID', () => {
    const state = { ...baseState, selectedShopId: 'sh1' };
    expect(selectSelectedShopId.projector(state)).toBe('sh1');
  });

  it('selectShopDetailsEntities returns detailsById map', () => {
    const state = { ...baseState, detailsById: { sh1: detailA } };
    expect(selectShopDetailsEntities.projector(state)).toEqual({ sh1: detailA });
  });

  it('selectSelectedShopDetails returns details for selected shop', () => {
    expect(selectSelectedShopDetails.projector('sh1', { sh1: detailA })).toEqual(detailA);
  });

  it('selectSelectedShopDetails returns null when no shop selected', () => {
    expect(selectSelectedShopDetails.projector(null, { sh1: detailA })).toBeNull();
  });

  it('selectSelectedShopDetails returns null when details not loaded', () => {
    expect(selectSelectedShopDetails.projector('sh99', {})).toBeNull();
  });

  it('selectShopsSubmitting reflects state', () => {
    const state = { ...baseState, submitting: true };
    expect(selectShopsSubmitting.projector(state)).toBe(true);
  });

  it('selectShopsErrorMessage reflects state', () => {
    const state = { ...baseState, errorMessage: 'oops' };
    expect(selectShopsErrorMessage.projector(state)).toBe('oops');
  });

  it('selectShopsLastMutationType reflects state', () => {
    const state = { ...baseState, lastMutationType: 'create' as const };
    expect(selectShopsLastMutationType.projector(state)).toBe('create');
  });

  it('selectShopsLastMutationSucceeded reflects state', () => {
    const state = { ...baseState, lastMutationSucceeded: true };
    expect(selectShopsLastMutationSucceeded.projector(state)).toBe(true);
  });
});
