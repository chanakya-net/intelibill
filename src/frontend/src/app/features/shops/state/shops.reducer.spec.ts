import { UserShop } from '../../../core/auth/auth.models';
import { ShopDetails } from '../services/shop.service';
import { ShopsActions } from './shops.actions';
import { ShopsState, shopsReducer } from './shops.reducer';

const shopA: UserShop = { shopId: 'sh1', shopName: 'Shop A', isDefault: true, role: 'Owner', lastUsedAt: null };
const shopB: UserShop = { shopId: 'sh2', shopName: 'Shop B', isDefault: false, role: 'Manager', lastUsedAt: null };

const detailA: ShopDetails = {
  shopId: 'sh1', name: 'Shop A', address: '1 Main', city: 'City', state: 'State', pincode: '560001',
  contactPerson: null, mobileNumber: null, gstNumber: null,
  bankName: null, bankAccountNumber: null, bankAccountType: null, ifscCode: null, accountHolderName: null,
};

describe('shopsReducer', () => {
  const initial = shopsReducer(undefined, { type: '@@INIT' } as never);

  it('sets loadingShops on loadShopsRequested', () => {
    const next = shopsReducer(initial, ShopsActions.loadShopsRequested());
    expect(next.loadingShops).toBe(true);
  });

  it('sets shops on loadShopsSucceeded', () => {
    const next = shopsReducer(initial, ShopsActions.loadShopsSucceeded({ shops: [shopA, shopB] }));
    expect(next.shops).toEqual([shopA, shopB]);
    expect(next.loadingShops).toBe(false);
  });

  it('sets errorMessage on loadShopsFailed', () => {
    const next = shopsReducer(initial, ShopsActions.loadShopsFailed({ errorMessage: 'err' }));
    expect(next.errorMessage).toBe('err');
    expect(next.loadingShops).toBe(false);
  });

  it('sets selectedShopId on selectShop', () => {
    const next = shopsReducer(initial, ShopsActions.selectShop({ shopId: 'sh1' }));
    expect(next.selectedShopId).toBe('sh1');
  });

  it('stores shop details on loadShopDetailsSucceeded', () => {
    const next = shopsReducer(initial, ShopsActions.loadShopDetailsSucceeded({ details: detailA }));
    expect(next.detailsById['sh1']).toEqual(detailA);
    expect(next.loadingDetails).toBe(false);
  });

  it('sets submitting and lastMutationType on createShopRequested', () => {
    const payload = { name: 'New', address: 'Addr', city: 'C', state: 'S', pincode: '560001' };
    const next = shopsReducer(initial, ShopsActions.createShopRequested({ payload }));
    expect(next.submitting).toBe(true);
    expect(next.lastMutationType).toBe('create');
  });

  it('marks success on createShopSucceeded', () => {
    const submitting = shopsReducer(initial, ShopsActions.createShopRequested({ payload: { name: 'N', address: 'A', city: 'C', state: 'S', pincode: '560001' } }));
    const next = shopsReducer(submitting, ShopsActions.createShopSucceeded());
    expect(next.submitting).toBe(false);
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('updates details on updateShopSucceeded', () => {
    const next = shopsReducer(initial, ShopsActions.updateShopSucceeded({ details: detailA }));
    expect(next.detailsById['sh1']).toEqual(detailA);
    expect(next.lastMutationType).toBe('update');
  });

  it('sets default mutation type on setDefaultShopRequested', () => {
    const next = shopsReducer(initial, ShopsActions.setDefaultShopRequested({ shopId: 'sh1' }));
    expect(next.lastMutationType).toBe('set-default');
    expect(next.submitting).toBe(true);
  });

  it('clears errorMessage on clearError', () => {
    const withError = shopsReducer(initial, ShopsActions.loadShopsFailed({ errorMessage: 'err' }));
    const next = shopsReducer(withError, ShopsActions.clearError());
    expect(next.errorMessage).toBe('');
  });

  it('clears mutation status on clearMutationStatus', () => {
    const withMutation = shopsReducer(initial, ShopsActions.createShopSucceeded());
    const next = shopsReducer(withMutation, ShopsActions.clearMutationStatus());
    expect(next.lastMutationType).toBeNull();
    expect(next.lastMutationSucceeded).toBe(false);
  });
});
