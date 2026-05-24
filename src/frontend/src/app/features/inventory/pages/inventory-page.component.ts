import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { AvatarModule } from 'primeng/avatar';
import { TagModule } from 'primeng/tag';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SkeletonModule } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddProductOverlayComponent } from '../components/add-product-overlay.component';
import { EditItemOverlayComponent } from '../components/edit-item-overlay.component';
import { InventoryFilterBarComponent } from '../components/inventory-filter-bar.component';
import { InventoryTableComponent } from '../components/inventory-table.component';
import type { Item } from '../services/inventory.models';
import { InventoryActions } from '../state/inventory.actions';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadingItems,
  selectInventorySubmitting,
} from '../state/inventory.selectors';

type ItemStatusFilter = 'all' | 'active' | 'inactive';

@Component({
  selector: 'app-inventory-page',
  standalone: true,
  imports: [
    FormsModule,
    BadgeModule,
    ButtonModule,
    CardModule,
    AvatarModule,
    TagModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SkeletonModule,
    TableModule,
    AddProductOverlayComponent,
    EditItemOverlayComponent,
    InventoryFilterBarComponent,
    InventoryTableComponent,
    TranslocoPipe,
  ],
  templateUrl: './inventory-page.component.html',
  styleUrl: './inventory-page.component.scss',
})
export class InventoryPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly items = this.store.selectSignal(selectInventoryItems);
  readonly searchValue = signal('');
  readonly statusFilter = signal<ItemStatusFilter>('all');
  readonly filteredItems = computed(() => {
    const q = this.searchValue().toLowerCase();
    const statusFiltered =
      this.statusFilter() === 'all' ? [...this.items()] : this.items().filter((i) => i.isActive === (this.statusFilter() === 'active'));

    if (!q) {
      return statusFiltered;
    }

    return statusFiltered.filter(
      (i) =>
        i.name.toLowerCase().includes(q) ||
        i.barcode.toLowerCase().includes(q) ||
        i.uom.toLowerCase().includes(q),
    );
  });

  readonly isLoadingItems = this.store.selectSignal(selectInventoryLoadingItems);
  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectInventoryLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectInventoryLastMutationSucceeded);

  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) {
      return '';
    }

    const activeShop = session.shops.find((shop) => shop.shopId === session.activeShopId) ?? session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canManageInventory = computed(() => {
    const role = this.activeShopRole().toLowerCase();
    return role === 'owner' || role === 'manager';
  });
  readonly showAddProductOverlay = signal(false);
  readonly showEditItemOverlay = signal(false);
  readonly selectedItemForEdit = signal<Item | null>(null);

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

      if (mutationType === 'update-item' && this.showEditItemOverlay()) {
        this.showEditItemOverlay.set(false);
        this.selectedItemForEdit.set(null);
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

  onCloseAddProduct(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.showAddProductOverlay.set(false);
  }

  onEditItem(item: Item): void {
    if (!this.canManageInventory()) {
      return;
    }

    this.store.dispatch(InventoryActions.clearError());
    this.store.dispatch(InventoryActions.clearMutationStatus());
    this.selectedItemForEdit.set(item);
    this.showEditItemOverlay.set(true);
  }

  onCloseEditItem(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.showEditItemOverlay.set(false);
  }

  onStatusFilterChange(statusFilter: ItemStatusFilter): void {
    this.statusFilter.set(statusFilter);
  }
}
