import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';

import { AuthService } from '../../../core/auth/auth.service';
import { AddSupplierOverlayComponent } from '../components/add-supplier-overlay.component';
import { EditSupplierOverlayComponent } from '../components/edit-supplier-overlay.component';
import { MakePaymentOverlayComponent } from '../components/make-payment-overlay.component';
import { SupplierDetailComponent } from '../components/supplier-detail.component';
import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import { SuppliersTableComponent } from '../components/suppliers-table.component';

@Component({
  selector: 'app-suppliers-page',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    SkeletonModule,
    AddSupplierOverlayComponent,
    EditSupplierOverlayComponent,
    MakePaymentOverlayComponent,
    SupplierDetailComponent,
    SuppliersTableComponent,
    TranslocoPipe,
  ],
  templateUrl: './suppliers-page.component.html',
  styleUrl: './suppliers-page.component.scss',
})
export class SuppliersPageComponent {
  private readonly authService = inject(AuthService);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly suppliers = this.suppliersFacade.suppliers;
  readonly userSuppliers = computed(() => this.suppliers().filter((s) => !s.isSystem));
  readonly totalSuppliers = computed(() => this.userSuppliers().length);
  readonly activeSuppliers = computed(() => this.userSuppliers().filter((s) => s.isActive).length);
  readonly preferredSuppliers = computed(() => this.userSuppliers().filter((s) => s.isPreferred).length);
  readonly pendingPayables = computed(() =>
    this.userSuppliers().reduce((total, supplier) => total + Math.max(supplier.balanceDue, 0), 0),
  );
  readonly isLoading = this.suppliersFacade.isLoading;
  readonly serverError = this.suppliersFacade.errorMessage;
  readonly lastMutationType = this.suppliersFacade.lastMutationType;
  readonly lastMutationSucceeded = this.suppliersFacade.lastMutationSucceeded;

  readonly showAddSupplierOverlay = signal(false);
  readonly showEditSupplierOverlay = signal(false);
  readonly showMakePaymentOverlay = signal(false);
  readonly showDetailModal = signal(false);
  readonly editingSupplier = signal<Supplier | null>(null);
  readonly paymentSupplier = signal<Supplier | null>(null);
  readonly detailSupplier = signal<Supplier | null>(null);
  readonly detailSupplierId = signal<string | null>(null);

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
  readonly canMakePayment = computed(() => ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()));

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
        return;
      }

      if (mutationType === 'make-payment' && this.showMakePaymentOverlay()) {
        this.showMakePaymentOverlay.set(false);
        this.paymentSupplier.set(null);
        this.suppliersFacade.clearMutationStatus();
        this.suppliersFacade.load();
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

  onOpenMakePayment(supplier: Supplier): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.paymentSupplier.set(supplier);
    this.showMakePaymentOverlay.set(true);
  }

  onCloseMakePayment(): void {
    this.showMakePaymentOverlay.set(false);
    this.paymentSupplier.set(null);
  }

  onOpenSupplierDetail(supplier: Supplier): void {
    this.detailSupplier.set(supplier);
    this.detailSupplierId.set(supplier.supplierId);
    this.showDetailModal.set(true);
  }

  onCloseSupplierDetail(): void {
    this.showDetailModal.set(false);
    this.detailSupplierId.set(null);
    this.detailSupplier.set(null);
  }

}
