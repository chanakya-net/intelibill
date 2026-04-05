import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { AddSupplierOverlayComponent } from '../components/add-supplier-overlay.component';
import { EditSupplierOverlayComponent } from '../components/edit-supplier-overlay.component';
import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';

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
  private readonly authService = inject(AuthService);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly suppliers = this.suppliersFacade.suppliers;
  readonly tableSuppliers = computed(() => [...this.suppliers()]);
  readonly isLoading = this.suppliersFacade.isLoading;
  readonly serverError = this.suppliersFacade.errorMessage;
  readonly lastMutationType = this.suppliersFacade.lastMutationType;
  readonly lastMutationSucceeded = this.suppliersFacade.lastMutationSucceeded;

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
    this.suppliersFacade.load();

    effect(() => {
      if (!this.lastMutationSucceeded()) {
        return;
      }

      const mutationType = this.lastMutationType();
      if (mutationType === 'add-supplier' && this.showAddSupplierOverlay()) {
        this.showAddSupplierOverlay.set(false);
        this.suppliersFacade.clearMutationStatus();
        return;
      }

      if (mutationType === 'edit-supplier' && this.showEditSupplierOverlay()) {
        this.showEditSupplierOverlay.set(false);
        this.editingSupplier.set(null);
        this.suppliersFacade.clearMutationStatus();
      }
    });
  }

  onOpenAddSupplier(): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.showAddSupplierOverlay.set(true);
  }

  onCloseAddSupplier(): void {
    this.showAddSupplierOverlay.set(false);
  }

  onOpenEditSupplier(supplier: Supplier): void {
    if (!this.canManageSuppliers()) {
      return;
    }

    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.editingSupplier.set(supplier);
    this.showEditSupplierOverlay.set(true);
  }

  onCloseEditSupplier(): void {
    this.showEditSupplierOverlay.set(false);
    this.editingSupplier.set(null);
  }
}