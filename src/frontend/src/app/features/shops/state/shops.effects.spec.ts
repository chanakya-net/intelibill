import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { AuthSession, UserShop } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { LeaveReadiness, ShopContextService } from '../../../core/context/shop-context.service';
import { ShopService } from '../services/shop.service';
import { ShopsActions } from './shops.actions';
import { ShopsEffects } from './shops.effects';

describe('ShopsEffects', () => {
  let actions$: Subject<Action>;
  let effects: ShopsEffects;

  const shopService = {
    getMyShops: vi.fn<ShopService['getMyShops']>(),
    getShopDetails: vi.fn<ShopService['getShopDetails']>(),
    createShop: vi.fn<ShopService['createShop']>(),
    updateShop: vi.fn<ShopService['updateShop']>(),
    setDefaultShop: vi.fn<ShopService['setDefaultShop']>(),
    switchActiveShop: vi.fn<ShopService['switchActiveShop']>(),
    updateBankDetails: vi.fn<ShopService['updateBankDetails']>(),
    updateTaxProfile: vi.fn<ShopService['updateTaxProfile']>(),
  };

  const sessionSignal = signal<AuthSession | null>(null);
  const authService = { session: sessionSignal };
  const shopContext = { prepareToLeave: vi.fn<() => Promise<LeaveReadiness>>() };

  const shopA: UserShop = { shopId: 'sh1', shopName: 'Shop A', isDefault: true, role: 'Owner', lastUsedAt: null };
  const details = {
    shopId: 'sh1', name: 'Shop A', address: '1 Main', city: 'C', state: 'S', pincode: '560001',
    contactPerson: null, mobileNumber: null, gstNumber: null,
    bankName: null, bankAccountNumber: null, bankAccountType: null, ifscCode: null, accountHolderName: null,
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    Object.values(shopService).forEach((fn) => fn.mockReset());
    shopContext.prepareToLeave.mockReset().mockResolvedValue({ ok: true });
    sessionSignal.set(null);

    TestBed.configureTestingModule({
      providers: [
        ShopsEffects,
        { provide: ShopService, useValue: shopService },
        { provide: AuthService, useValue: authService },
        { provide: ShopContextService, useValue: shopContext },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(ShopsEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches loadShopsSucceeded on load success', async () => {
    shopService.getMyShops.mockReturnValue(of([shopA]));

    const output = firstValueFrom(effects.loadShops$.pipe(take(1)));
    actions$.next(ShopsActions.loadShopsRequested());

    await expect(output).resolves.toEqual(ShopsActions.loadShopsSucceeded({ shops: [shopA] }));
  });

  it('dispatches loadShopsFailed on load error', async () => {
    shopService.getMyShops.mockReturnValue(throwError(() => ({})));

    const output = firstValueFrom(effects.loadShops$.pipe(take(1)));
    actions$.next(ShopsActions.loadShopsRequested());

    await expect(output).resolves.toEqual(
      ShopsActions.loadShopsFailed({ errorMessage: 'errors.shops.unableToLoadShops' })
    );
  });

  it('dispatches loadShopDetailsSucceeded on details load', async () => {
    shopService.getShopDetails.mockReturnValue(of(details));

    const output = firstValueFrom(effects.loadShopDetails$.pipe(take(1)));
    actions$.next(ShopsActions.loadShopDetailsRequested({ shopId: 'sh1' }));

    await expect(output).resolves.toEqual(ShopsActions.loadShopDetailsSucceeded({ details }));
  });

  it('auto-loads details for default shop after loadShopsSucceeded', async () => {
    const output = firstValueFrom(effects.loadActiveShopDetailsAfterShopsLoad$.pipe(take(1)));
    actions$.next(ShopsActions.loadShopsSucceeded({ shops: [shopA] }));

    await expect(output).resolves.toEqual(ShopsActions.loadShopDetailsRequested({ shopId: 'sh1' }));
  });

  it('dispatches createShopSucceeded on create success', async () => {
    (shopService.createShop as ReturnType<typeof vi.fn>).mockReturnValue(of(undefined));
    const payload = { name: 'New', address: 'A', city: 'C', state: 'S', pincode: '560001' };

    const output = firstValueFrom(effects.createShop$.pipe(take(1)));
    actions$.next(ShopsActions.createShopRequested({ payload }));

    await expect(output).resolves.toEqual(ShopsActions.createShopSucceeded());
  });

  it('dispatches createShopFailed with nameRequired error', async () => {
    shopService.createShop.mockReturnValue(
      throwError(() => ({ error: { title: 'Shop.NameRequired' } }))
    );

    const output = firstValueFrom(effects.createShop$.pipe(take(1)));
    actions$.next(ShopsActions.createShopRequested({ payload: { name: '', address: 'A', city: 'C', state: 'S', pincode: '560001' } }));

    await expect(output).resolves.toEqual(
      ShopsActions.createShopFailed({ errorMessage: 'errors.shops.nameRequired' })
    );
  });

  it.each([
    ['Shop.TaxRegimeRequired', 'errors.shops.taxRegimeRequired'],
    ['Shop.StateCodeInvalid', 'errors.shops.stateCodeInvalid'],
    ['Shop.GstinStateMismatch', 'errors.shops.gstinStateMismatch'],
    ['Shop.EInvoiceAttestationRequired', 'errors.shops.eInvoiceAttestationRequired'],
  ])('explains a refused tax profile on a new shop (%s)', async (code, key) => {
    shopService.createShop.mockReturnValue(throwError(() => ({ error: { title: code } })));

    const output = firstValueFrom(effects.createShop$.pipe(take(1)));
    actions$.next(ShopsActions.createShopRequested({ payload: { name: 'New', address: 'A', city: 'C', state: 'S', pincode: '560001' } }));

    await expect(output).resolves.toEqual(ShopsActions.createShopFailed({ errorMessage: key }));
  });

  it('saves the tax profile and hands back the declared details', async () => {
    const declared = {
      ...details,
      gstNumber: '27AAPFU0939F1ZV',
      taxProfile: {
        legalName: 'Shop A LLP',
        stateCode: '27',
        taxRegime: 'Regular' as const,
        invoiceSeriesCode: 'A',
        eInvoiceStatus: 'BelowThreshold' as const,
        eInvoiceAttestedAt: '2026-11-03T05:30:00Z',
      },
    };
    const payload = {
      legalName: 'Shop A LLP',
      stateCode: '27',
      taxRegime: 'Regular' as const,
      gstNumber: '27AAPFU0939F1ZV',
      invoiceSeriesCode: null,
      eInvoiceStatus: 'BelowThreshold' as const,
    };
    shopService.updateTaxProfile.mockReturnValue(of(declared));

    const output = firstValueFrom(effects.updateShopTaxProfile$.pipe(take(1)));
    actions$.next(ShopsActions.updateShopTaxProfileRequested({ shopId: 'sh1', payload }));

    await expect(output).resolves.toEqual(ShopsActions.updateShopTaxProfileSucceeded({ details: declared }));
    expect(shopService.updateTaxProfile).toHaveBeenCalledWith('sh1', payload);
  });

  it.each([
    ['Shop.GstinInvalid', 'errors.shops.gstinInvalid'],
    ['Shop.GstinRequiredForRegime', 'errors.shops.gstinRequiredForRegime'],
    ['Shop.GstinNotAllowedForUnregistered', 'errors.shops.gstinNotAllowedForUnregistered'],
    ['Shop.InvoiceSeriesCodeInvalid', 'errors.shops.invoiceSeriesCodeInvalid'],
    ['Shop.LegalNameTooLong', 'errors.shops.legalNameTooLong'],
    ['Shop.UserIsNotOwner', 'errors.shops.onlyOwnersCanUpdate'],
    ['Something.Unexpected', 'errors.shops.unableToUpdateTaxProfile'],
  ])('explains a refused tax profile save (%s)', async (code, key) => {
    shopService.updateTaxProfile.mockReturnValue(throwError(() => ({ error: { title: code } })));

    const output = firstValueFrom(effects.updateShopTaxProfile$.pipe(take(1)));
    actions$.next(
      ShopsActions.updateShopTaxProfileRequested({
        shopId: 'sh1',
        payload: { legalName: null, stateCode: '27', taxRegime: 'Unregistered', gstNumber: null, invoiceSeriesCode: null, eInvoiceStatus: null },
      }),
    );

    await expect(output).resolves.toEqual(ShopsActions.updateShopTaxProfileFailed({ errorMessage: key }));
  });

  it('dispatches setDefaultShopSucceeded on success', async () => {
    (shopService.setDefaultShop as ReturnType<typeof vi.fn>).mockReturnValue(of(undefined));

    const output = firstValueFrom(effects.setDefaultShop$.pipe(take(1)));
    actions$.next(ShopsActions.setDefaultShopRequested({ shopId: 'sh1' }));

    await expect(output).resolves.toEqual(ShopsActions.setDefaultShopSucceeded());
  });

  it('dispatches switchActiveShopSucceeded on switch success', async () => {
    (shopService.switchActiveShop as ReturnType<typeof vi.fn>).mockReturnValue(of(undefined));

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));

    await expect(output).resolves.toEqual(ShopsActions.switchActiveShopSucceeded({ shopId: 'sh2' }));
    expect(shopService.switchActiveShop).toHaveBeenCalledWith('sh2');
  });

  it('dispatches switchActiveShopFailed when the membership is gone', async () => {
    shopService.switchActiveShop.mockReturnValue(
      throwError(() => ({ error: { title: 'Shop.MembershipNotFound' } }))
    );

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));

    await expect(output).resolves.toEqual(
      ShopsActions.switchActiveShopFailed({ errorMessage: 'errors.shops.membershipNotFound' })
    );
  });

  it('dispatches switchActiveShopFailed when the device session is gone', async () => {
    shopService.switchActiveShop.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.SessionInvalidated' } }))
    );

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));

    await expect(output).resolves.toEqual(
      ShopsActions.switchActiveShopFailed({ errorMessage: 'errors.auth.sessionVerificationFailed' })
    );
  });

  it('dispatches switchActiveShopFailed with a generic message otherwise', async () => {
    shopService.switchActiveShop.mockReturnValue(throwError(() => ({})));

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));

    await expect(output).resolves.toEqual(
      ShopsActions.switchActiveShopFailed({ errorMessage: 'errors.shops.unableToSwitchStore' })
    );
  });

  it('switch_waits_for_leave_readiness: refuses to switch while an owner-bound write is in flight', async () => {
    shopContext.prepareToLeave.mockResolvedValue({ ok: false, reason: 'mutation-in-flight' });

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));

    await expect(output).resolves.toEqual(
      ShopsActions.switchActiveShopFailed({ errorMessage: 'errors.shops.switchBlockedByPendingOperation' })
    );
    expect(shopService.switchActiveShop).not.toHaveBeenCalled();
  });

  it('refuses to switch when the cart could not be saved under the shop being left', async () => {
    shopContext.prepareToLeave.mockResolvedValue({ ok: false, reason: 'draft-save-failed' });

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));

    await expect(output).resolves.toEqual(
      ShopsActions.switchActiveShopFailed({ errorMessage: 'errors.shops.switchDraftSaveFailed' })
    );
    expect(shopService.switchActiveShop).not.toHaveBeenCalled();
  });

  it('refuses to create a shop, which moves the device, while an owner-bound write is in flight', async () => {
    shopContext.prepareToLeave.mockResolvedValue({ ok: false, reason: 'mutation-in-flight' });

    const output = firstValueFrom(effects.createShop$.pipe(take(1)));
    actions$.next(
      ShopsActions.createShopRequested({
        payload: { name: 'New', address: 'A', city: 'C', state: 'S', pincode: '560001' },
      })
    );

    await expect(output).resolves.toEqual(
      ShopsActions.createShopFailed({ errorMessage: 'errors.shops.switchBlockedByPendingOperation' })
    );
    expect(shopService.createShop).not.toHaveBeenCalled();
  });

  it('ignores a second switch request while the first is still being prepared', async () => {
    let ready!: (readiness: LeaveReadiness) => void;
    shopContext.prepareToLeave.mockReturnValue(new Promise((resolve) => (ready = resolve)));
    shopService.switchActiveShop.mockReturnValue(of(undefined));

    const output = firstValueFrom(effects.switchActiveShop$.pipe(take(1)));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh2' }));
    actions$.next(ShopsActions.switchActiveShopRequested({ shopId: 'sh3' }));
    ready({ ok: true });

    await expect(output).resolves.toEqual(ShopsActions.switchActiveShopSucceeded({ shopId: 'sh2' }));
    expect(shopService.switchActiveShop).toHaveBeenCalledTimes(1);
  });

  it('auto-loads details for the shop this device is in, not the default one', async () => {
    const shopB: UserShop = { shopId: 'sh2', shopName: 'Shop B', isDefault: false, role: 'Manager', lastUsedAt: null };
    sessionSignal.set({
      accessToken: 'a',
      refreshToken: 'r',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: { id: 'u1', email: 'u@example.test', phoneNumber: null, firstName: 'U', lastName: 'One' },
      activeShopId: 'sh2',
      shops: [shopA, shopB],
    });

    const output = firstValueFrom(effects.loadActiveShopDetailsAfterShopsLoad$.pipe(take(1)));
    actions$.next(ShopsActions.loadShopsSucceeded({ shops: [shopA, shopB] }));

    await expect(output).resolves.toEqual(ShopsActions.loadShopDetailsRequested({ shopId: 'sh2' }));
  });

  it('refreshes shops after successful mutations', async () => {
    const output = firstValueFrom(effects.refreshShopsAfterMutation$.pipe(take(1)));
    actions$.next(ShopsActions.createShopSucceeded());

    await expect(output).resolves.toEqual(ShopsActions.loadShopsRequested());
  });
});
