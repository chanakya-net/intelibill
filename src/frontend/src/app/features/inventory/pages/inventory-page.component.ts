import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddProductOverlayComponent } from '../components/add-product-overlay.component';
import { InventoryActions } from '../state/inventory.actions';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadingItems,
  selectInventorySubmitting,
} from '../state/inventory.selectors';

@Component({
  selector: 'app-inventory-page',
  standalone: true,
  imports: [CommonModule, BadgeModule, ButtonModule, ProgressSpinnerModule, TableModule, AddProductOverlayComponent, TranslocoPipe],
  templateUrl: './inventory-page.component.html',
  styleUrl: './inventory-page.component.scss',
})
export class InventoryPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly items = this.store.selectSignal(selectInventoryItems);
  readonly tableItems = computed(() => [...this.items()]);
  readonly isLoadingItems = this.store.selectSignal(selectInventoryLoadingItems);
  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectInventoryLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectInventoryLastMutationSucceeded);
  readonly showAddProductOverlay = signal(false);

  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) {
      return '';
    }

    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canManageInventory = computed(() => {
    const role = this.activeShopRole().toLowerCase();
    return role === 'owner' || role === 'manager';
  });

  constructor() {
    this.store.dispatch(InventoryActions.loadItemsRequested());
    this.store.dispatch(InventoryActions.clearMutationStatus());

    effect(() => {
      if (!this.lastMutationSucceeded()) {
        return;
      }

      const mutationType = this.lastMutationType();
      if (mutationType === 'add-item' && this.showAddProductOverlay()) {
        this.showAddProductOverlay.set(false);
        this.store.dispatch(InventoryActions.clearMutationStatus());
      }
    });
  }

  onOpenAddProduct(): void {
    if (!this.canManageInventory()) {
      return;
    }

    this.store.dispatch(InventoryActions.clearError());
    this.store.dispatch(InventoryActions.clearMutationStatus());
    this.showAddProductOverlay.set(true);
  }

  stockSeverity(stock: number): 'danger' | 'warn' | 'success' {
    if (stock <= 5) return 'danger';
    if (stock < 50) return 'warn';
    return 'success';
  }

  onCloseAddProduct(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.showAddProductOverlay.set(false);
  }
}
