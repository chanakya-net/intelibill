import { SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { Supplier, SupplierStatus } from '../services/supplier.service';
import { SuppliersState } from './suppliers.reducer';
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

const supplierA: Supplier = {
  supplierId: 's1',
  name: 'Fresh Foods',
  contactPersonName: 'Ramesh',
  contactPersonPhone: '+919999999999',
  address: 'Address',
  city: 'City',
  state: 'State',
  pin: '560001',
  amount: 500,
  status: SupplierStatus.IWillReceive,
  isActive: true,
  isPreferred: false,
  balanceDue: 500,
};

const supplierB: Supplier = {
  supplierId: 's2',
  name: 'Green Retail',
  contactPersonName: null,
  contactPersonPhone: null,
  address: 'Address',
  city: 'City',
  state: 'State',
  pin: '560002',
  amount: 200,
  status: SupplierStatus.INeedToPay,
  isActive: true,
  isPreferred: true,
  balanceDue: 200,
};

const entries: readonly SupplierLedgerEntry[] = [
  {
    id: 'l1',
    supplierId: 's1',
    entryType: 'GOODS_RECEIVED',
    amount: 200,
    entryDate: '2026-04-01T00:00:00Z',
    notes: null,
  },
];

describe('suppliers selectors', () => {
  const suppliersState: SuppliersState = {
    ids: ['s1', 's2'],
    entities: {
      s1: supplierA,
      s2: supplierB,
    },
    loadingSuppliers: true,
    submitting: true,
    errorMessage: 'errors.suppliers.unableToLoadSuppliers',
    lastMutationType: 'edit-supplier',
    lastMutationSucceeded: true,
    currentLedgerSupplierId: 's1',
    ledgerEntries: entries,
    loadingLedger: true,
    ledgerErrorMessage: 'errors.suppliers.unableToLoadLedger',
  };

  const rootState = {
    suppliers: suppliersState,
  };

  it('selects suppliers list', () => {
    expect(selectSuppliers(rootState as never)).toEqual([supplierA, supplierB]);
  });

  it('selects supplier entities', () => {
    expect(selectSupplierEntities(rootState as never)).toEqual({ s1: supplierA, s2: supplierB });
  });

  it('selects suppliers status flags', () => {
    expect(selectSuppliersLoading(rootState as never)).toBe(true);
    expect(selectSuppliersSubmitting(rootState as never)).toBe(true);
    expect(selectSuppliersErrorMessage(rootState as never)).toBe('errors.suppliers.unableToLoadSuppliers');
    expect(selectSuppliersLastMutationType(rootState as never)).toBe('edit-supplier');
    expect(selectSuppliersLastMutationSucceeded(rootState as never)).toBe(true);
  });

  it('selects ledger state', () => {
    expect(selectLedgerEntries(rootState as never)).toEqual(entries);
    expect(selectLedgerLoading(rootState as never)).toBe(true);
    expect(selectLedgerErrorMessage(rootState as never)).toBe('errors.suppliers.unableToLoadLedger');
  });
});
