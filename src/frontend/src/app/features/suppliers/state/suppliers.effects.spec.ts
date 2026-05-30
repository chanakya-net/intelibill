import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { SupplierLedgerService } from '../services/supplier-ledger.service';
import { SupplierService } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';
import { SuppliersEffects } from './suppliers.effects';

describe('SuppliersEffects', () => {
  let actions$: Subject<Action>;
  let effects: SuppliersEffects;

  const supplierService = {
    getSuppliers: vi.fn<SupplierService['getSuppliers']>(),
    addSupplier: vi.fn<SupplierService['addSupplier']>(),
    editSupplier: vi.fn<SupplierService['editSupplier']>(),
  };

  const ledgerService = {
    getSupplierLedgerEntries: vi.fn<SupplierLedgerService['getSupplierLedgerEntries']>(),
    makePayment: vi.fn<SupplierLedgerService['makePayment']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    supplierService.getSuppliers.mockReset();
    supplierService.addSupplier.mockReset();
    supplierService.editSupplier.mockReset();
    ledgerService.getSupplierLedgerEntries.mockReset();
    ledgerService.makePayment.mockReset();

    TestBed.configureTestingModule({
      providers: [
        SuppliersEffects,
        { provide: SupplierService, useValue: supplierService },
        { provide: SupplierLedgerService, useValue: ledgerService },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(SuppliersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches loadSuppliersSucceeded on load success', async () => {
    supplierService.getSuppliers.mockReturnValue(
      of([
        {
          supplierId: 's1',
          name: 'Fresh Foods',
          contactPersonName: 'Ramesh',
          contactPersonPhone: '+919999999999',
          address: 'Address',
          city: 'City',
          state: 'State',
          pin: '560001',
          isSystem: false,
          isActive: true,
          isPreferred: false,
          balanceDue: 1500,
        },
      ])
    );

    const output = firstValueFrom(effects.loadSuppliers$.pipe(take(1)));
    actions$.next(SuppliersActions.loadSuppliersRequested());

    await expect(output).resolves.toEqual(
      SuppliersActions.loadSuppliersSucceeded({
        suppliers: [
          {
            supplierId: 's1',
            name: 'Fresh Foods',
            contactPersonName: 'Ramesh',
            contactPersonPhone: '+919999999999',
            address: 'Address',
            city: 'City',
            state: 'State',
            pin: '560001',
            isSystem: false,
            isActive: true,
            isPreferred: false,
            balanceDue: 1500,
          },
        ],
      })
    );
  });

  it('maps owner restriction on add supplier failure', async () => {
    supplierService.addSupplier.mockReturnValue(
      throwError(() => ({ error: { title: 'Supplier.UserIsNotOwner' } }))
    );

    const output = firstValueFrom(effects.addSupplier$.pipe(take(1)));
    actions$.next(
      SuppliersActions.addSupplierRequested({
        payload: {
          name: 'Supplier',
          contactPersonName: null,
          contactPersonPhone: null,
          address: 'Address',
          city: 'City',
          state: 'State',
          pin: '560001',
          isActive: true,
          isPreferred: false,
        },
      })
    );

    await expect(output).resolves.toEqual(
      SuppliersActions.addSupplierFailed({
        errorMessage: 'errors.suppliers.onlyOwnerCanManageSuppliers',
      })
    );
  });

  it('dispatches makePaymentSucceeded on payment success', async () => {
    const entry = {
      id: 'e1',
      supplierId: 's1',
      entryType: 'PAYMENT_MADE' as const,
      amount: 500,
      entryDate: '2026-04-07',
      notes: null,
    };
    ledgerService.makePayment.mockReturnValue(of(entry));

    const output = firstValueFrom(effects.makePayment$.pipe(take(1)));
    actions$.next(SuppliersActions.makePaymentRequested({
      supplierId: 's1',
      payload: { amount: 500, paymentDate: '2026-04-07', notes: null },
    }));

    await expect(output).resolves.toEqual(SuppliersActions.makePaymentSucceeded({ entry }));
  });

  it('maps authorization error on make payment failure', async () => {
    ledgerService.makePayment.mockReturnValue(
      throwError(() => ({ error: { title: 'Supplier.UserIsNotOwnerOrManager' } }))
    );

    const output = firstValueFrom(effects.makePayment$.pipe(take(1)));
    actions$.next(SuppliersActions.makePaymentRequested({
      supplierId: 's1',
      payload: { amount: 500, paymentDate: '2026-04-07', notes: null },
    }));

    await expect(output).resolves.toEqual(
      SuppliersActions.makePaymentFailed({
        errorMessage: 'errors.suppliers.onlyOwnerOrManagerCanMakePayment',
      })
    );
  });

  it('maps generic error on make payment failure', async () => {
    ledgerService.makePayment.mockReturnValue(
      throwError(() => ({ error: { title: 'SomeUnknownError' } }))
    );

    const output = firstValueFrom(effects.makePayment$.pipe(take(1)));
    actions$.next(SuppliersActions.makePaymentRequested({
      supplierId: 's1',
      payload: { amount: 500, paymentDate: '2026-04-07', notes: null },
    }));

    await expect(output).resolves.toEqual(
      SuppliersActions.makePaymentFailed({
        errorMessage: 'errors.suppliers.unableToMakePayment',
      })
    );
  });
});
