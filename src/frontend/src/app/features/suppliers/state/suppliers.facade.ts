import {
  PartyDirectoryQuery,
  DEFAULT_DIRECTORY_QUERY,
} from '../../../shared/models/party-directory.models';
import {
  selectDirectoryQuery,
  selectDirectoryTotalCount,
  selectDirectorySummary,
} from './suppliers.selectors';
import { Injectable, Signal, inject } from '@angular/core';
import { Dictionary } from '@ngrx/entity';
import { Store } from '@ngrx/store';

import { MakePaymentRequest } from '../services/supplier-ledger.service';
import { AddSupplierRequest, EditSupplierRequest, Supplier } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';
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

@Injectable({ providedIn: 'root' })
export class SuppliersFacade {
  private readonly store = inject(Store);
  readonly query = this.store.selectSignal(selectDirectoryQuery);
  readonly totalCount = this.store.selectSignal(selectDirectoryTotalCount);
  readonly summary = this.store.selectSignal(selectDirectorySummary);
  loadDirectory(query: PartyDirectoryQuery = this.query() ?? DEFAULT_DIRECTORY_QUERY): void {
    this.store.dispatch(
      SuppliersActions.directoryRequested({ query, requestId: crypto.randomUUID() }),
    );
  }

  readonly suppliers: Signal<readonly Supplier[]> = this.store.selectSignal(selectSuppliers);
  readonly supplierEntities: Signal<Dictionary<Supplier>> =
    this.store.selectSignal(selectSupplierEntities);
  readonly isLoading: Signal<boolean> = this.store.selectSignal(selectSuppliersLoading);
  readonly isSubmitting: Signal<boolean> = this.store.selectSignal(selectSuppliersSubmitting);
  readonly errorMessage: Signal<string> = this.store.selectSignal(selectSuppliersErrorMessage);
  readonly lastMutationType: Signal<'add-supplier' | 'edit-supplier' | 'make-payment' | null> =
    this.store.selectSignal(selectSuppliersLastMutationType);
  readonly lastMutationSucceeded: Signal<boolean> = this.store.selectSignal(
    selectSuppliersLastMutationSucceeded,
  );

  readonly ledgerEntries = this.store.selectSignal(selectLedgerEntries);
  readonly ledgerIsLoading = this.store.selectSignal(selectLedgerLoading);
  readonly ledgerErrorMessage = this.store.selectSignal(selectLedgerErrorMessage);

  load(): void {
    this.loadDirectory();
  }

  clearError(): void {
    this.store.dispatch(SuppliersActions.clearError());
  }

  clearMutationStatus(): void {
    this.store.dispatch(SuppliersActions.clearMutationStatus());
  }

  addSupplier(payload: AddSupplierRequest): void {
    this.store.dispatch(SuppliersActions.addSupplierRequested({ payload }));
  }

  editSupplier(supplierId: string, payload: EditSupplierRequest): void {
    this.store.dispatch(SuppliersActions.editSupplierRequested({ supplierId, payload }));
  }

  makePayment(supplierId: string, payload: MakePaymentRequest): void {
    this.store.dispatch(SuppliersActions.makePaymentRequested({ supplierId, payload }));
  }

  /** Resends the unconfirmed payment for this supplier, unchanged and under its own key. */
  retryPayment(supplierId: string): void {
    this.store.dispatch(SuppliersActions.retryPaymentRequested({ supplierId }));
  }

  loadLedger(supplierId: string): void {
    this.store.dispatch(SuppliersActions.loadSupplierLedgerRequested({ supplierId }));
  }

  clearLedger(): void {
    this.store.dispatch(SuppliersActions.clearLedger());
  }
}
