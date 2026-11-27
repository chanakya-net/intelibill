import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { firstValueFrom, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { OperatingAccessHintService } from '../../billing/status/operating-access-hint.service';
import { SupplierLedgerService } from '../services/supplier-ledger.service';
import { SupplierService } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';
import { SuppliersEffects } from './suppliers.effects';

describe('Supplier licensing effects', () => {
  const scope = signal({ userId: 'user-1', shopId: 'shop-1', epoch: 1 });
  const supplierService = {
    addSupplier: vi.fn<SupplierService['addSupplier']>(),
    editSupplier: vi.fn<SupplierService['editSupplier']>(),
  };
  let actions$: Subject<Action>;
  let effects: SuppliersEffects;

  beforeEach(() => {
    scope.set({ userId: 'user-1', shopId: 'shop-1', epoch: 1 });
    supplierService.addSupplier.mockReset();
    supplierService.editSupplier.mockReset();
    actions$ = new Subject<Action>();

    TestBed.configureTestingModule({
      providers: [
        SuppliersEffects,
        { provide: Actions, useFactory: () => new Actions(actions$) },
        { provide: ShopContextService, useValue: { scope } },
        { provide: SupplierService, useValue: supplierService },
        { provide: SupplierLedgerService, useValue: {} },
        { provide: AuthService, useValue: {} },
        { provide: FinancialOperationIntentService, useValue: {} },
      ],
    });
    effects = TestBed.inject(SuppliersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('reports typed permanent and transient licence refusals in the correct UI state', async () => {
    const access = TestBed.inject(OperatingAccessHintService);
    supplierService.addSupplier.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409, error: { code: 'Licence.Busy' },
    })));
    const busy = firstValueFrom(effects.addSupplier$);
    actions$.next(SuppliersActions.addSupplierRequested({ payload: {} as never }));
    expect(await busy).toEqual(SuppliersActions.addSupplierFailed({
      errorMessage: 'billing.status.tradingAuthority.busy',
    }));
    expect(access.blockingMessage()).toBeNull();

    supplierService.editSupplier.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 403, error: { code: 'Licence.Expired' },
    })));
    const expired = firstValueFrom(effects.editSupplier$);
    actions$.next(SuppliersActions.editSupplierRequested({ supplierId: 's1', payload: {} as never }));
    expect(await expired).toEqual(SuppliersActions.editSupplierFailed({ errorMessage: '' }));
    expect(access.blockingMessage()).toBe('billing.status.tradingAuthority.expired');
  });

  it('drops late supplier successes and refusals after a change of scope', () => {
    const writing = new Subject<never>();
    const editing = new Subject<never>();
    supplierService.addSupplier.mockReturnValueOnce(writing);
    supplierService.editSupplier.mockReturnValueOnce(editing);
    const dispatch = vi.fn();
    const add = effects.addSupplier$.subscribe(dispatch);
    const edit = effects.editSupplier$.subscribe(dispatch);
    actions$.next(SuppliersActions.addSupplierRequested({ payload: {} as never }));
    actions$.next(SuppliersActions.editSupplierRequested({ supplierId: 's1', payload: {} as never }));

    scope.set({ ...scope(), epoch: 2 });
    writing.next({} as never);
    editing.error(new HttpErrorResponse({ status: 403, error: { code: 'Licence.Expired' } }));

    expect(dispatch).not.toHaveBeenCalled();
    expect(TestBed.inject(OperatingAccessHintService).blockingMessage()).toBeNull();
    add.unsubscribe();
    edit.unsubscribe();
  });
});
