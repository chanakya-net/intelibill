import {
  PartyDirectoryQuery,
  SupplierDirectoryPage,
} from '../../../shared/models/party-directory.models';
import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { MakePaymentRequest, SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { AddSupplierRequest, EditSupplierRequest, Supplier } from '../services/supplier.service';

export type SupplierMutationType = 'add-supplier' | 'edit-supplier' | 'make-payment';

export const SuppliersActions = createActionGroup({
  source: 'Suppliers',
  events: {
    'Directory Requested': props<{ query: PartyDirectoryQuery; requestId: string }>(),
    'Directory Succeeded': props<{ page: SupplierDirectoryPage; requestId: string }>(),
    'Directory Failed': props<{ errorMessage: string; requestId: string }>(),
    'Load Suppliers Requested': emptyProps(),
    'Load Suppliers Succeeded': props<{ suppliers: readonly Supplier[] }>(),
    'Load Suppliers Failed': props<{ errorMessage: string }>(),

    'Add Supplier Requested': props<{ payload: AddSupplierRequest }>(),
    'Add Supplier Succeeded': props<{ supplier: Supplier }>(),
    'Add Supplier Failed': props<{ errorMessage: string }>(),

    'Edit Supplier Requested': props<{ supplierId: string; payload: EditSupplierRequest }>(),
    'Edit Supplier Succeeded': props<{ supplier: Supplier }>(),
    'Edit Supplier Failed': props<{ errorMessage: string }>(),

    'Make Payment Requested': props<{ supplierId: string; payload: MakePaymentRequest }>(),
    'Retry Payment Requested': props<{ supplierId: string }>(),
    'Make Payment Succeeded': props<{ entry: SupplierLedgerEntry }>(),
    'Make Payment Failed': props<{ errorMessage: string }>(),

    'Load Supplier Ledger Requested': props<{ supplierId: string }>(),
    'Load Supplier Ledger Succeeded': props<{
      supplierId: string;
      entries: readonly SupplierLedgerEntry[];
    }>(),
    'Load Supplier Ledger Failed': props<{ errorMessage: string }>(),
    'Clear Ledger': emptyProps(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
