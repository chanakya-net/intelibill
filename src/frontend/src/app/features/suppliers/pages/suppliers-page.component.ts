import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddSupplierOverlayComponent } from '../components/add-supplier-overlay.component';
import { EditSupplierOverlayComponent } from '../components/edit-supplier-overlay.component';
import { Supplier } from '../services/supplier.service';
import { SuppliersActions } from '../state/suppliers.actions';
import {
  selectSuppliers,
  selectSuppliersErrorMessage,
  selectSuppliersLastMutationSucceeded,
  selectSuppliersLastMutationType,
  selectSuppliersLoading,
} from '../state/suppliers.selectors';

@Component({
  selector: 'app-suppliers-page',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    ProgressSpinnerModule,
    TableModule,
    AddSupplierOverlayComponent,
    EditSupplierOverlayComponent,
    TranslocoPipe,
  ],
  templateUrl: './suppliers-page.component.html',
  styleUrl: './suppliers-page.component.scss',
})
export class SuppliersPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly suppliers = this.store.selectSignal(selectSuppliers);
  readonly tableSuppliers = computed(() => [...this.suppliers()]);
  readonly isLoading = this.store.selectSignal(selectSuppliersLoading);
  readonly serverError = this.store.selectSignal(selectSuppliersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectSuppliersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectSuppliersLastMutationSucceeded);

  readonly showAddSupplierOverlay = signal(false);
  readonly showEditSupplierOverlay = signal(false);
  readonly editingSupplier = signal<Supplier | null>(null);

  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) {
      return '';
    }

    const activeShop = session.shops.find((shop) => shop.shopId === session.activeShopId) ?? session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canManageSuppliers = computed(() => this.activeShopRole().toLowerCase() === 'owner');

  constructor() {
    this.store.dispatch(SuppliersActions.loadSuppliersRequested());

    effect(() => {
      if (!this.lastMutationSucceeded()) {
        return;
      }

      const mutationType = this.lastMutationType();
      if (mutationType === 'add-supplier' && this.showAddSupplierOverlay()) {
        this.showAddSupplierOverlay.set(false);
        this.store.dispatch(SuppliersActions.clearMutationStatus());
        return;
      }

      if (mutationType === 'edit-supplier' && this.showEditSupplierOverlay()) {
        this.showEditSupplierOverlay.set(false);
        this.editingSupplier.set(null);
        this.store.dispatch(SuppliersActions.clearMutationStatus());
      }
    });
  }

  onOpenAddSupplier(): void {
    this.store.dispatch(SuppliersActions.clearError());
    this.store.dispatch(SuppliersActions.clearMutationStatus());
    this.showAddSupplierOverlay.set(true);
  }

  onCloseAddSupplier(): void {
    this.showAddSupplierOverlay.set(false);
  }

  onOpenEditSupplier(supplier: Supplier): void {
    if (!this.canManageSuppliers()) {
      return;
    }

    this.store.dispatch(SuppliersActions.clearError());
    this.store.dispatch(SuppliersActions.clearMutationStatus());
    this.editingSupplier.set(supplier);
    this.showEditSupplierOverlay.set(true);
  }

  onCloseEditSupplier(): void {
    this.showEditSupplierOverlay.set(false);
    this.editingSupplier.set(null);
  }
}