import { Injectable, Signal, inject } from '@angular/core';
import { Dictionary } from '@ngrx/entity';
import { Store } from '@ngrx/store';

import { AddSupplierRequest, EditSupplierRequest, Supplier } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';
import {
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

  readonly suppliers: Signal<readonly Supplier[]> = this.store.selectSignal(selectSuppliers);
  readonly supplierEntities: Signal<Dictionary<Supplier>> = this.store.selectSignal(selectSupplierEntities);
  readonly isLoading: Signal<boolean> = this.store.selectSignal(selectSuppliersLoading);
  readonly isSubmitting: Signal<boolean> = this.store.selectSignal(selectSuppliersSubmitting);
  readonly errorMessage: Signal<string> = this.store.selectSignal(selectSuppliersErrorMessage);
  readonly lastMutationType: Signal<'add-supplier' | 'edit-supplier' | null> =
    this.store.selectSignal(selectSuppliersLastMutationType);
  readonly lastMutationSucceeded: Signal<boolean> = this.store.selectSignal(selectSuppliersLastMutationSucceeded);

  load(): void {
    this.store.dispatch(SuppliersActions.loadSuppliersRequested());
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
}