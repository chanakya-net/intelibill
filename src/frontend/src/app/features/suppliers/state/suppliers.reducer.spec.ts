import { SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { Supplier, SupplierStatus } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';
import { suppliersReducer } from './suppliers.reducer';

const supplierA: Supplier = {
  supplierId: 's1',
  name: 'Fresh Foods',
  contactPersonName: 'Ramesh',
  contactPersonPhone: '+919999999999',
  address: 'Address 1',
  city: 'City',
  state: 'State',
  pin: '560001',
  amount: 1500,
  status: SupplierStatus.IWillReceive,
  isActive: true,
  isPreferred: false,
  balanceDue: 1500,
};

const supplierB: Supplier = {
  supplierId: 's2',
  name: 'Alpha Traders',
  contactPersonName: null,
  contactPersonPhone: null,
  address: 'Address 2',
  city: 'City',
  state: 'State',
  pin: '560002',
  amount: 100,
  status: SupplierStatus.INeedToPay,
  isActive: true,
  isPreferred: true,
  balanceDue: 100,
};

const ledgerEntries: readonly SupplierLedgerEntry[] = [
  {
    id: 'l1',
    supplierId: 's1',
    entryType: 'GOODS_RECEIVED',
    amount: 400,
    entryDate: '2026-04-01T00:00:00Z',
    notes: null,
  },
];

describe('suppliersReducer', () => {
  const initialState = suppliersReducer(undefined, { type: '@@INIT' } as never);

  it('sets and sorts suppliers when load succeeds', () => {
    const next = suppliersReducer(
      initialState,
      SuppliersActions.loadSuppliersSucceeded({ suppliers: [supplierA, supplierB] })
    );

    expect(next.ids).toEqual(['s2', 's1']);
    expect(next.entities['s1']).toEqual(supplierA);
    expect(next.entities['s2']).toEqual(supplierB);
  });

  it('adds supplier on add success', () => {
    const state = suppliersReducer(initialState, SuppliersActions.addSupplierRequested({ payload: {
      name: 'Any',
      contactPersonName: null,
      contactPersonPhone: null,
      address: 'Address',
      city: 'City',
      state: 'State',
      pin: '560001',
      amount: 0,
      status: SupplierStatus.IWillReceive,
      isActive: true,
      isPreferred: false,
    } }));

    const next = suppliersReducer(state, SuppliersActions.addSupplierSucceeded({ supplier: supplierA }));

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('add-supplier');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.entities['s1']).toEqual(supplierA);
  });

  it('updates supplier on edit success', () => {
    const withSupplier = suppliersReducer(
      initialState,
      SuppliersActions.loadSuppliersSucceeded({ suppliers: [supplierA] })
    );

    const next = suppliersReducer(
      withSupplier,
      SuppliersActions.editSupplierSucceeded({
        supplier: {
          ...supplierA,
          isPreferred: true,
        },
      })
    );

    expect(next.entities['s1']?.isPreferred).toBe(true);
    expect(next.lastMutationType).toBe('edit-supplier');
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('handles ledger load lifecycle', () => {
    const loading = suppliersReducer(
      initialState,
      SuppliersActions.loadSupplierLedgerRequested({ supplierId: 's1' })
    );

    const loaded = suppliersReducer(
      loading,
      SuppliersActions.loadSupplierLedgerSucceeded({ supplierId: 's1', entries: ledgerEntries })
    );

    const cleared = suppliersReducer(loaded, SuppliersActions.clearLedger());

    expect(loading.loadingLedger).toBe(true);
    expect(loading.currentLedgerSupplierId).toBe('s1');
    expect(loaded.loadingLedger).toBe(false);
    expect(loaded.ledgerEntries).toEqual(ledgerEntries);
    expect(cleared.currentLedgerSupplierId).toBeNull();
    expect(cleared.ledgerEntries).toEqual([]);
  });
});
