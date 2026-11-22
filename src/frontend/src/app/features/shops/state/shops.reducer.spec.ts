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

  it('tracks a tax profile save and stores the details it returns', () => {
    const payload = {
      legalName: null,
      stateCode: '27',
      taxRegime: 'Unregistered' as const,
      gstNumber: null,
      invoiceSeriesCode: null,
      eInvoiceStatus: null,
    };
    const submitting = shopsReducer(initial, ShopsActions.updateShopTaxProfileRequested({ shopId: 'sh1', payload }));
    expect(submitting.submitting).toBe(true);
    expect(submitting.lastMutationType).toBe('update-tax-profile');

    const declared: ShopDetails = {
      ...detailA,
      taxProfile: {
        legalName: null,
        stateCode: '27',
        taxRegime: 'Unregistered',
        invoiceSeriesCode: 'A',
        eInvoiceStatus: 'NotAttested',
        eInvoiceAttestedAt: null,
      },
    };
    const saved = shopsReducer(submitting, ShopsActions.updateShopTaxProfileSucceeded({ details: declared }));
    expect(saved.submitting).toBe(false);
    expect(saved.lastMutationSucceeded).toBe(true);
    expect(saved.detailsById['sh1']).toEqual(declared);

    const failed = shopsReducer(submitting, ShopsActions.updateShopTaxProfileFailed({ errorMessage: 'errors.shops.gstinInvalid' }));
    expect(failed.submitting).toBe(false);
    expect(failed.errorMessage).toBe('errors.shops.gstinInvalid');
    expect(failed.lastMutationType).toBe('update-tax-profile');
    expect(failed.lastMutationSucceeded).toBe(false);
  });

  it('marks a switch in flight on switchActiveShopRequested', () => {
    const next = shopsReducer(initial, ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));
    expect(next.submitting).toBe(true);
    expect(next.lastMutationType).toBe('switch');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('clears the in-flight switch on switchActiveShopSucceeded', () => {
    const requested = shopsReducer(initial, ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));
    const next = shopsReducer(requested, ShopsActions.switchActiveShopSucceeded({ shopId: 'sh2' }));
    expect(next.submitting).toBe(false);
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('keeps the shop picker usable after a failed switch', () => {
    const requested = shopsReducer(initial, ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));
    const next = shopsReducer(
      requested,
      ShopsActions.switchActiveShopFailed({ errorMessage: 'errors.shops.unableToSwitchStore' })
    );
    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('errors.shops.unableToSwitchStore');
    expect(next.lastMutationSucceeded).toBe(false);
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

  describe('detail reads overtaken by a licence refusal', () => {
    const detailB: ShopDetails = { ...detailA, shopId: 'sh2', name: 'Shop B' };
    const access = { contractVersion: 1, shopId: 'sh1', enforced: true, status: 'Active', writableProfileFields: ['name'],
      canPrepareTax: true, canSavePreferences: true, canEditOperatingSettings: true, hasOperatingAccess: true,
      canCreateAdditionalShop: true } as const;
    const saved: ShopDetails = { ...detailA, setupAccess: access };
    const refusal = { shopId: 'sh1', securitySuspended: true, errorMessage: 'security' };
    const opened = shopsReducer(initial, ShopsActions.loadShopDetailsSucceeded({ details: saved }));
    const pending = shopsReducer(opened, ShopsActions.loadShopDetailsRequested({ shopId: 'sh1' }));

    it('keeps a security-refused shop absent when the earlier read answers late', () => {
      const refused = shopsReducer(pending, ShopsActions.shopLicenceRefused(refusal));
      expect(refused.detailsById['sh1']).toBeUndefined();

      const late = shopsReducer(refused, ShopsActions.loadShopDetailsSucceeded({ details: saved }));
      expect(late.detailsById['sh1']).toBeUndefined();
      expect(late.errorMessage).toBe('security');
      expect(late.loadingDetails).toBe(false);
    });

    it('applies the same rule to a refusal from a shop mutation', () => {
      const refused = shopsReducer(pending, ShopsActions.updateShopFailed({
        errorMessage: 'security', shopId: 'sh1', licenceRefused: true, securitySuspended: true,
      }));
      const late = shopsReducer(refused, ShopsActions.loadShopDetailsSucceeded({ details: saved }));
      expect(late.detailsById['sh1']).toBeUndefined();
    });

    it('does not let an earlier read restore permissions after a trading refusal', () => {
      const refused = shopsReducer(pending, ShopsActions.shopLicenceRefused({ ...refusal, securitySuspended: false }));
      expect(refused.detailsById['sh1']?.setupAccess).toBeNull();

      const late = shopsReducer(refused, ShopsActions.loadShopDetailsSucceeded({ details: saved }));
      expect(late.detailsById['sh1']?.setupAccess).toBeNull();
    });

    it('accepts a read requested after the refusal', () => {
      const refused = shopsReducer(pending, ShopsActions.shopLicenceRefused(refusal));
      const requested = shopsReducer(refused, ShopsActions.loadShopDetailsRequested({ shopId: 'sh1' }));
      const fresh = shopsReducer(requested, ShopsActions.loadShopDetailsSucceeded({ details: saved }));
      expect(fresh.detailsById['sh1']).toEqual(saved);
      expect(fresh.errorMessage).toBe('');
    });

    it('still accepts results for an unrelated shop', () => {
      const refused = shopsReducer(pending, ShopsActions.shopLicenceRefused(refusal));
      const other = shopsReducer(refused, ShopsActions.loadShopDetailsSucceeded({ details: detailB }));
      expect(other.detailsById['sh2']).toEqual(detailB);
      expect(other.detailsById['sh1']).toBeUndefined();
    });

    it('leaves a read for another shop untouched by the refusal', () => {
      const other = shopsReducer(opened, ShopsActions.loadShopDetailsRequested({ shopId: 'sh2' }));
      const refused = shopsReducer(other, ShopsActions.shopLicenceRefused(refusal));
      const answered = shopsReducer(refused, ShopsActions.loadShopDetailsSucceeded({ details: detailB }));
      expect(answered.detailsById['sh2']).toEqual(detailB);
    });
  });

  describe('saves overtaken by a licence refusal', () => {
    const detailB: ShopDetails = { ...detailA, shopId: 'sh2', name: 'Shop B' };
    const access = { contractVersion: 1, shopId: 'sh1', enforced: true, status: 'Active', writableProfileFields: ['name'],
      canPrepareTax: true, canSavePreferences: true, canEditOperatingSettings: true, hasOperatingAccess: true,
      canCreateAdditionalShop: true } as const;
    const saved: ShopDetails = { ...detailA, setupAccess: access };
    const refusal = { shopId: 'sh1', securitySuspended: true, errorMessage: 'security' };
    const opened = shopsReducer(initial, ShopsActions.loadShopDetailsSucceeded({ details: saved }));
    const profile = { name: 'Shop A', address: '1 Main', city: 'City', state: 'State', pincode: '560001' };
    const tax = { legalName: null, stateCode: '27', taxRegime: 'Unregistered' as const, gstNumber: null,
      invoiceSeriesCode: null, eInvoiceStatus: null };
    const bank = { bankName: 'Bank', accountNumber: '1', accountType: 'Current', ifscCode: 'ABCD0123456', accountHolderName: 'A' };
    const saves = {
      profile: (shopId: string, details: ShopDetails) => [
        ShopsActions.updateShopRequested({ shopId, payload: profile }), ShopsActions.updateShopSucceeded({ details })] as const,
      tax: (shopId: string, details: ShopDetails) => [
        ShopsActions.updateShopTaxProfileRequested({ shopId, payload: tax }),
        ShopsActions.updateShopTaxProfileSucceeded({ details })] as const,
      bank: (shopId: string, details: ShopDetails) => [
        ShopsActions.updateShopBankDetailsRequested({ shopId, payload: bank as never }),
        ShopsActions.updateShopBankDetailsSucceeded({ details })] as const,
    };
    const kinds = ['profile', 'tax', 'bank'] as const;

    it.each(kinds)('keeps a security-refused shop absent when its %s save answers late', (kind) => {
      const [requested, succeeded] = saves[kind]('sh1', saved);
      const refused = shopsReducer(shopsReducer(opened, requested), ShopsActions.shopLicenceRefused(refusal));
      const late = shopsReducer(refused, succeeded);

      expect(late.detailsById['sh1']).toBeUndefined();
      expect(late.errorMessage).toBe('security');
      expect(late.submitting).toBe(false);
      expect(late.lastMutationSucceeded).toBe(true);
    });

    it.each(kinds)('does not let a late %s save restore permissions after a trading refusal', (kind) => {
      const [requested, succeeded] = saves[kind]('sh1', saved);
      const refused = shopsReducer(shopsReducer(opened, requested),
        ShopsActions.shopLicenceRefused({ ...refusal, securitySuspended: false }));
      const late = shopsReducer(refused, succeeded);

      expect(late.detailsById['sh1']?.setupAccess).toBeNull();
    });

    it('treats a refused detail read as the same invalidation', () => {
      const [requested, succeeded] = saves.profile('sh1', saved);
      const refused = shopsReducer(shopsReducer(opened, requested), ShopsActions.loadShopDetailsFailed({
        errorMessage: 'security', shopId: 'sh1', licenceRefused: true, securitySuspended: true,
      }));
      const late = shopsReducer(refused, succeeded);

      expect(late.detailsById['sh1']).toBeUndefined();
    });

    it('lets a fresh authorized read restore the shop afterwards', () => {
      const [requested, succeeded] = saves.tax('sh1', saved);
      const refused = shopsReducer(shopsReducer(opened, requested), ShopsActions.shopLicenceRefused(refusal));
      const late = shopsReducer(refused, succeeded);
      const reading = shopsReducer(late, ShopsActions.loadShopDetailsRequested({ shopId: 'sh1' }));
      const fresh = shopsReducer(reading, ShopsActions.loadShopDetailsSucceeded({ details: saved }));

      expect(fresh.detailsById['sh1']).toEqual(saved);
      expect(fresh.errorMessage).toBe('');
    });

    it('publishes a save requested after the refusal', () => {
      const [requested, succeeded] = saves.profile('sh1', saved);
      const refused = shopsReducer(opened, ShopsActions.shopLicenceRefused({ ...refusal, securitySuspended: false }));
      const next = shopsReducer(shopsReducer(refused, requested), succeeded);

      expect(next.detailsById['sh1']).toEqual(saved);
    });

    it('leaves a save for another shop unaffected', () => {
      const [requested, succeeded] = saves.bank('sh2', detailB);
      const refused = shopsReducer(shopsReducer(opened, requested), ShopsActions.shopLicenceRefused(refusal));
      const answered = shopsReducer(refused, succeeded);

      expect(answered.detailsById['sh2']).toEqual(detailB);
      expect(answered.detailsById['sh1']).toBeUndefined();
      expect(answered.errorMessage).toBe('');
    });
  });
});
