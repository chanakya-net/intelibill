import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { SuppliersActions } from './suppliers.actions';
import { SuppliersFacade } from './suppliers.facade';
import {
  selectLedgerEntries,
  selectLedgerErrorMessage,
  selectLedgerLoading,
  selectSupplierEntities,
  selectSuppliers,
  selectSuppliersErrorMessage,
  selectSuppliersLastMutationSucceeded,
  selectSuppliersLastMutationType,
  selectSuppliersLoading,
  selectSuppliersSubmitting,
} from './suppliers.selectors';

describe('SuppliersFacade', () => {
  const dispatch = vi.fn();
  const suppliersSignal = signal([
    {
      supplierId: 's1',
      name: 'Fresh Foods',
      contactPersonName: 'Ramesh',
      contactPersonPhone: '+919999999999',
      address: 'Address',
      city: 'City',
      state: 'State',
      pin: '560001',
      isActive: true,
      isPreferred: false,
      balanceDue: 500,
    },
  ]);
  const entitiesSignal = signal({
    s1: suppliersSignal()[0],
  });
  const boolSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'add-supplier' | 'edit-supplier' | null>(null);
  const ledgerEntriesSignal = signal([]);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectSuppliers) {
        return suppliersSignal;
      }
      if (selector === selectSupplierEntities) {
        return entitiesSignal;
      }
      if (selector === selectSuppliersLoading || selector === selectSuppliersSubmitting) {
        return boolSignal;
      }
      if (selector === selectSuppliersErrorMessage || selector === selectLedgerErrorMessage) {
        return errorSignal;
      }
      if (selector === selectSuppliersLastMutationType) {
        return lastMutationTypeSignal;
      }
      if (selector === selectSuppliersLastMutationSucceeded || selector === selectLedgerLoading) {
        return boolSignal;
      }
      if (selector === selectLedgerEntries) {
        return ledgerEntriesSignal;
      }
      return signal(undefined);
    }),
  };

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();

    TestBed.configureTestingModule({
      providers: [SuppliersFacade, { provide: Store, useValue: store }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('exposes selector-backed signals', () => {
    const facade = TestBed.inject(SuppliersFacade);

    expect(facade.suppliers().length).toBe(1);
    expect(facade.supplierEntities()['s1']?.name).toBe('Fresh Foods');
    expect(facade.ledgerEntries()).toEqual([]);
  });

  it('dispatches suppliers actions through facade methods', () => {
    const facade = TestBed.inject(SuppliersFacade);

    facade.load();
    facade.clearError();
    facade.clearMutationStatus();
    facade.addSupplier({
      name: 'New Supplier',
      contactPersonName: null,
      contactPersonPhone: null,
      address: 'Address',
      city: 'City',
      state: 'State',
      pin: '560001',
      isActive: true,
      isPreferred: false,
    });
    facade.editSupplier('s1', {
      name: 'Fresh Foods Updated',
      contactPersonName: null,
      contactPersonPhone: null,
      address: 'Address',
      city: 'City',
      state: 'State',
      pin: '560001',
      isActive: true,
      isPreferred: true,
    });
    facade.loadLedger('s1');
    facade.clearLedger();

    expect(dispatch).toHaveBeenCalledWith(SuppliersActions.loadSuppliersRequested());
    expect(dispatch).toHaveBeenCalledWith(SuppliersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(SuppliersActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      SuppliersActions.addSupplierRequested({
        payload: {
          name: 'New Supplier',
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
    expect(dispatch).toHaveBeenCalledWith(
      SuppliersActions.editSupplierRequested({
        supplierId: 's1',
        payload: {
          name: 'Fresh Foods Updated',
          contactPersonName: null,
          contactPersonPhone: null,
          address: 'Address',
          city: 'City',
          state: 'State',
          pin: '560001',
          isActive: true,
          isPreferred: true,
        },
      })
    );
    expect(dispatch).toHaveBeenCalledWith(
      SuppliersActions.loadSupplierLedgerRequested({ supplierId: 's1' })
    );
    expect(dispatch).toHaveBeenCalledWith(SuppliersActions.clearLedger());
  });
});
