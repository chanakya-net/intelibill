import { createFeature, createReducer, on } from '@ngrx/store';

import { Supplier } from '../services/supplier.service';
import { SupplierMutationType, SuppliersActions } from './suppliers.actions';

export const suppliersFeatureKey = 'suppliers';

export interface SuppliersState {
  readonly suppliers: readonly Supplier[];
  readonly loadingSuppliers: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: SupplierMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: SuppliersState = {
  suppliers: [],
  loadingSuppliers: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
};

export const suppliersReducer = createReducer(
  initialState,
  on(SuppliersActions.loadSuppliersRequested, (state) => ({
    ...state,
    loadingSuppliers: true,
    errorMessage: '',
  })),
  on(SuppliersActions.loadSuppliersSucceeded, (state, { suppliers }) => ({
    ...state,
    loadingSuppliers: false,
    suppliers,
    errorMessage: '',
  })),
  on(SuppliersActions.loadSuppliersFailed, (state, { errorMessage }) => ({
    ...state,
    loadingSuppliers: false,
    errorMessage,
  })),

  on(SuppliersActions.addSupplierRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'add-supplier',
    lastMutationSucceeded: false,
  })),
  on(SuppliersActions.addSupplierSucceeded, (state, { supplier }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    suppliers: [...state.suppliers, supplier],
    lastMutationType: 'add-supplier',
    lastMutationSucceeded: true,
  })),
  on(SuppliersActions.addSupplierFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'add-supplier',
    lastMutationSucceeded: false,
  })),

  on(SuppliersActions.editSupplierRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'edit-supplier',
    lastMutationSucceeded: false,
  })),
  on(SuppliersActions.editSupplierSucceeded, (state, { supplier }) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    suppliers: state.suppliers.map((current) => (current.supplierId === supplier.supplierId ? supplier : current)),
    lastMutationType: 'edit-supplier',
    lastMutationSucceeded: true,
  })),
  on(SuppliersActions.editSupplierFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'edit-supplier',
    lastMutationSucceeded: false,
  })),

  on(SuppliersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(SuppliersActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  }))
);

export const suppliersFeature = createFeature({
  name: suppliersFeatureKey,
  reducer: suppliersReducer,
});