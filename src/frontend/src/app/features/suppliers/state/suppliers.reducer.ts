import { ShopContextActions } from '../../../core/context/shop-context.actions';
import {
  PartyDirectoryQuery,
  SupplierDirectorySummary,
  EMPTY_SUPPLIER_SUMMARY,
  DEFAULT_DIRECTORY_QUERY,
} from '../../../shared/models/party-directory.models';
import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { Supplier } from '../services/supplier.service';
import { SupplierMutationType, SuppliersActions } from './suppliers.actions';

export const suppliersFeatureKey = 'suppliers';

export const suppliersAdapter = createEntityAdapter<Supplier>({
  selectId: (supplier) => supplier.supplierId,
  sortComparer: (left, right) => left.name.localeCompare(right.name),
});

export interface SuppliersState extends EntityState<Supplier> {
  readonly query: PartyDirectoryQuery;
  readonly requestId: string | undefined;
  readonly totalCount: number;
  readonly summary: SupplierDirectorySummary;
  readonly loadingSuppliers: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: SupplierMutationType | null;
  readonly lastMutationSucceeded: boolean;
  readonly currentLedgerSupplierId: string | null;
  readonly ledgerEntries: readonly SupplierLedgerEntry[];
  readonly loadingLedger: boolean;
  readonly ledgerErrorMessage: string;
}

const initialState: SuppliersState = suppliersAdapter.getInitialState({
  query: DEFAULT_DIRECTORY_QUERY,
  requestId: undefined,
  totalCount: 0,
  summary: EMPTY_SUPPLIER_SUMMARY,
  loadingSuppliers: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  currentLedgerSupplierId: null,
  ledgerEntries: [],
  loadingLedger: false,
  ledgerErrorMessage: '',
});

export const suppliersReducer = createReducer(
  initialState,
  on(ShopContextActions.scopeChanged, () => initialState),
  on(SuppliersActions.directoryRequested, (state, { query, requestId }) => ({
    ...state,
    query,
    requestId,
    loadingSuppliers: true,
    errorMessage: '',
  })),
  on(SuppliersActions.directorySucceeded, (state, { page, requestId }) =>
    requestId !== state.requestId
      ? state
      : suppliersAdapter.setAll([...page.items], {
          ...state,
          totalCount: page.totalCount,
          summary: page.summary,
          query: { ...state.query, pageNumber: page.pageNumber, pageSize: page.pageSize },
          loadingSuppliers: false,
          errorMessage: '',
        }),
  ),
  on(SuppliersActions.directoryFailed, (state, { errorMessage, requestId }) =>
    requestId !== state.requestId ? state : { ...state, loadingSuppliers: false, errorMessage },
  ),
  on(SuppliersActions.loadSuppliersRequested, (state) => ({
    ...state,
    loadingSuppliers: true,
    errorMessage: '',
  })),
  on(SuppliersActions.loadSuppliersSucceeded, (state, { suppliers }) =>
    suppliersAdapter.setAll([...suppliers], {
      ...state,
      loadingSuppliers: false,
      errorMessage: '',
    }),
  ),
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
  on(SuppliersActions.addSupplierSucceeded, (state, { supplier }) =>
    suppliersAdapter.addOne(supplier, {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationType: 'add-supplier',
      lastMutationSucceeded: true,
    }),
  ),
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
  on(SuppliersActions.editSupplierSucceeded, (state, { supplier }) =>
    suppliersAdapter.updateOne(
      {
        id: supplier.supplierId,
        changes: supplier,
      },
      {
        ...state,
        submitting: false,
        errorMessage: '',
        lastMutationType: 'edit-supplier',
        lastMutationSucceeded: true,
      },
    ),
  ),
  on(SuppliersActions.editSupplierFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'edit-supplier',
    lastMutationSucceeded: false,
  })),

  on(SuppliersActions.makePaymentRequested, SuppliersActions.retryPaymentRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'make-payment' as const,
    lastMutationSucceeded: false,
  })),
  on(SuppliersActions.makePaymentSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'make-payment' as const,
    lastMutationSucceeded: true,
  })),
  on(SuppliersActions.makePaymentFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'make-payment' as const,
    lastMutationSucceeded: false,
  })),

  on(SuppliersActions.loadSupplierLedgerRequested, (state, { supplierId }) => ({
    ...state,
    loadingLedger: true,
    currentLedgerSupplierId: supplierId,
    ledgerErrorMessage: '',
  })),
  on(SuppliersActions.loadSupplierLedgerSucceeded, (state, { entries }) => ({
    ...state,
    loadingLedger: false,
    ledgerEntries: entries,
  })),
  on(SuppliersActions.loadSupplierLedgerFailed, (state, { errorMessage }) => ({
    ...state,
    loadingLedger: false,
    ledgerEntries: [],
    ledgerErrorMessage: errorMessage,
  })),
  on(SuppliersActions.clearLedger, (state) => ({
    ...state,
    currentLedgerSupplierId: null,
    ledgerEntries: [],
    loadingLedger: false,
    ledgerErrorMessage: '',
  })),

  on(SuppliersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(SuppliersActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  })),
);

export const suppliersFeature = createFeature({
  name: suppliersFeatureKey,
  reducer: suppliersReducer,
});
