import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { UserShop } from '../../../core/auth/auth.models';
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
    updateBankDetails: vi.fn<ShopService['updateBankDetails']>(),
  };

  const shopA: UserShop = { shopId: 'sh1', shopName: 'Shop A', isDefault: true, role: 'Owner', lastUsedAt: null };
  const details = {
    shopId: 'sh1', name: 'Shop A', address: '1 Main', city: 'C', state: 'S', pincode: '560001',
    contactPerson: null, mobileNumber: null, gstNumber: null,
    bankName: null, bankAccountNumber: null, bankAccountType: null, ifscCode: null, accountHolderName: null,
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    Object.values(shopService).forEach((fn) => fn.mockReset());

    TestBed.configureTestingModule({
      providers: [
        ShopsEffects,
        { provide: ShopService, useValue: shopService },
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

  it('dispatches setDefaultShopSucceeded on success', async () => {
    (shopService.setDefaultShop as ReturnType<typeof vi.fn>).mockReturnValue(of(undefined));

    const output = firstValueFrom(effects.setDefaultShop$.pipe(take(1)));
    actions$.next(ShopsActions.setDefaultShopRequested({ shopId: 'sh1' }));

    await expect(output).resolves.toEqual(ShopsActions.setDefaultShopSucceeded());
  });

  it('refreshes shops after successful mutations', async () => {
    const output = firstValueFrom(effects.refreshShopsAfterMutation$.pipe(take(1)));
    actions$.next(ShopsActions.createShopSucceeded());

    await expect(output).resolves.toEqual(ShopsActions.loadShopsRequested());
  });
});
