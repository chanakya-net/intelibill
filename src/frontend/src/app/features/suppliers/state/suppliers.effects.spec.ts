import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
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

  let originalLocalStorage: PropertyDescriptor | undefined;

  beforeEach(() => {
    originalLocalStorage = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    const values = new Map<string, string>();
    Object.defineProperty(globalThis, 'localStorage', {
      configurable: true,
      value: {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => void values.set(key, value),
        removeItem: (key: string) => void values.delete(key),
        clear: () => values.clear(),
      },
    });
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
        { provide: AuthService, useValue: { session: () => ({ user: { id: 'user-1' }, activeShopId: 'shop-1' }) } },
        provideHttpClient(),
        provideHttpClientTesting(),
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
    if (originalLocalStorage) {
      Object.defineProperty(globalThis, 'localStorage', originalLocalStorage);
    } else {
      delete (globalThis as { localStorage?: Storage }).localStorage;
    }
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
      throwError(() => new HttpErrorResponse({ status: 403, error: { title: 'Supplier.UserIsNotOwnerOrManager' } }))
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
      throwError(() => new HttpErrorResponse({ status: 400, error: { title: 'SomeUnknownError' } }))
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

  it('sends a new payment with a durable operation key', async () => {
    ledgerService.makePayment.mockReturnValue(of({ id: 'e1', supplierId: 's1', entryType: 2, amount: 500, entryDate: '2026-04-07', notes: null }));

    const output = firstValueFrom(effects.makePayment$.pipe(take(1)));
    actions$.next(SuppliersActions.makePaymentRequested({
      supplierId: 's1',
      payload: { amount: 500, paymentDate: '2026-04-07', notes: null },
    }));
    await output;

    const [, sent] = ledgerService.makePayment.mock.calls[0];
    expect(sent).toEqual(expect.objectContaining({ amount: 500, operationKey: expect.stringMatching(/^supplier-payment-/) }));
  });

  it('keeps a lost payment pending, blocks a new one and retries the same key', async () => {
    const intents = TestBed.inject(FinancialOperationIntentService);
    ledgerService.makePayment.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));

    const lost = firstValueFrom(effects.makePayment$.pipe(take(1)));
    actions$.next(SuppliersActions.makePaymentRequested({
      supplierId: 's1',
      payload: { amount: 500, paymentDate: '2026-04-07', notes: null },
    }));
    await expect(lost).resolves.toEqual(
      SuppliersActions.makePaymentFailed({ errorMessage: 'financialOperations.outcomeUnknown' })
    );
    const firstKey = ledgerService.makePayment.mock.calls[0][1].operationKey;
    expect(intents.pending({ userId: 'user-1', shopId: 'shop-1' }, 'supplier-payment', 's1')?.status).toBe('unknown');

    const blocked = firstValueFrom(effects.makePayment$.pipe(take(1)));
    actions$.next(SuppliersActions.makePaymentRequested({
      supplierId: 's1',
      payload: { amount: 650, paymentDate: '2026-04-07', notes: null },
    }));
    await expect(blocked).resolves.toEqual(
      SuppliersActions.makePaymentFailed({ errorMessage: 'financialOperations.pendingBlocksNew' })
    );
    expect(ledgerService.makePayment).toHaveBeenCalledTimes(1);

    const entry = { id: 'e1', supplierId: 's1', entryType: 2 as const, amount: 500, entryDate: '2026-04-07', notes: null };
    ledgerService.makePayment.mockReturnValueOnce(of(entry));
    const retried = firstValueFrom(effects.retryPayment$.pipe(take(1)));
    actions$.next(SuppliersActions.retryPaymentRequested({ supplierId: 's1' }));

    await expect(retried).resolves.toEqual(SuppliersActions.makePaymentSucceeded({ entry }));
    expect(ledgerService.makePayment.mock.calls[1][1]).toEqual({
      amount: 500,
      paymentDate: '2026-04-07',
      notes: null,
      operationKey: firstKey,
    });
    expect(intents.pending({ userId: 'user-1', shopId: 'shop-1' }, 'supplier-payment', 's1')).toBeNull();
  });
});
