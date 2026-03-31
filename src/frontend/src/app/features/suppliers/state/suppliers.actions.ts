import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { AddSupplierRequest, EditSupplierRequest, Supplier } from '../services/supplier.service';

export type SupplierMutationType = 'add-supplier' | 'edit-supplier';

export const SuppliersActions = createActionGroup({
  source: 'Suppliers',
  events: {
    'Load Suppliers Requested': emptyProps(),
    'Load Suppliers Succeeded': props<{ suppliers: readonly Supplier[] }>(),
    'Load Suppliers Failed': props<{ errorMessage: string }>(),

    'Add Supplier Requested': props<{ payload: AddSupplierRequest }>(),
    'Add Supplier Succeeded': props<{ supplier: Supplier }>(),
    'Add Supplier Failed': props<{ errorMessage: string }>(),

    'Edit Supplier Requested': props<{ supplierId: string; payload: EditSupplierRequest }>(),
    'Edit Supplier Succeeded': props<{ supplier: Supplier }>(),
    'Edit Supplier Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});