import { Component, computed, effect, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
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
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { AddProductOverlayComponent } from '../components/add-product-overlay.component';
import {
  BarcodeLabelPrintCandidate,
  BarcodeLabelPrintDialogComponent,
} from '../components/barcode-label-print-dialog.component';
import { EditItemOverlayComponent } from '../components/edit-item-overlay.component';
import { InventoryFilterBarComponent } from '../components/inventory-filter-bar.component';
import { InventoryTableComponent } from '../components/inventory-table.component';
import type { Item } from '../services/inventory.models';
import type { ItemCatalogStatusFilter } from '../services/inventory.models';
import type { BarcodeLabelPrintRequest } from '../services/inventory.models';
import { InventoryService } from '../services/inventory.service';
import { BlobDownloadService } from '../../../shared/utils/blob-download.service';
import { InventoryActions } from '../state/inventory.actions';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastAddedItem,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadingItems,
  selectInventorySubmitting,
  selectInventoryPagination,
  selectInventorySummary,
} from '../state/inventory.selectors';

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
    ConfirmDialogModule,
    AddProductOverlayComponent,
    BarcodeLabelPrintDialogComponent,
    EditItemOverlayComponent,
    InventoryFilterBarComponent,
    InventoryTableComponent,
    TranslocoPipe,
    DecimalPipe,
  ],
  providers: [ConfirmationService],
  templateUrl: './inventory-page.component.html',
  styleUrl: './inventory-page.component.scss',
})
export class InventoryPageComponent {
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);
  private readonly inventoryService = inject(InventoryService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly blobDownloadService = inject(BlobDownloadService);

  readonly items = this.store.selectSignal(selectInventoryItems);
  readonly searchValue = signal('');
  readonly statusFilter = signal<ItemCatalogStatusFilter>('all');
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);

  readonly pagination = this.store.selectSignal(selectInventoryPagination);
  readonly summary = this.store.selectSignal(selectInventorySummary);

  readonly footerStart = computed(() => {
    const total = this.pagination().totalCount;
    if (total === 0) return 0;
    return (this.pageNumber() - 1) * this.pageSize() + 1;
  });

  readonly footerEnd = computed(() => {
    const total = this.pagination().totalCount;
    const end = this.pageNumber() * this.pageSize();
    return end > total ? total : end;
  });

  readonly isLoadingItems = this.store.selectSignal(selectInventoryLoadingItems);
  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectInventoryLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectInventoryLastMutationSucceeded);
  readonly lastAddedItem = this.store.selectSignal(selectInventoryLastAddedItem);

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

  readonly selectedCatalogItems = signal<readonly Item[]>([]);
  readonly printDialogVisible = signal(false);
  readonly printDialogCandidates = signal<readonly BarcodeLabelPrintCandidate[]>([]);

  private searchTimeout: any;

  constructor() {
    this.loadItems();
    this.store.dispatch(InventoryActions.clearMutationStatus());

    effect(() => {
      if (!this.lastMutationSucceeded()) {
        return;
      }

      const mutationType = this.lastMutationType();
      if (mutationType === 'add-item' && this.showAddProductOverlay()) {
        this.showAddProductOverlay.set(false);

        const item = this.lastAddedItem();
        if (item) {
          this.confirmationService.confirm({
            message: 'inventory.printBarcode.prompt.message',
            header: 'inventory.printBarcode.prompt.header',
            icon: 'pi pi-barcode',
            acceptButtonStyleClass: 'p-button-primary',
            rejectButtonStyleClass: 'p-button-secondary p-button-text',
            accept: () => {
              this.openPrintDialog([item]);
            },
          });
        }

        this.store.dispatch(InventoryActions.clearMutationStatus());
      }

      if (mutationType === 'update-item' && this.showEditItemOverlay()) {
        this.showEditItemOverlay.set(false);
        this.selectedItemForEdit.set(null);
        this.store.dispatch(InventoryActions.clearMutationStatus());
      }
    });
  }

  loadItems(): void {
    this.store.dispatch(
      InventoryActions.loadItemsRequested({
        query: {
          search: this.searchValue(),
          status: this.statusFilter(),
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize(),
        },
      })
    );
  }

  onSearchChange(value: string): void {
    this.searchValue.set(value);

    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    this.searchTimeout = setTimeout(() => {
      this.pageNumber.set(1);
      this.loadItems();
    }, 280);
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

  onStatusFilterChange(statusFilter: ItemCatalogStatusFilter): void {
    this.statusFilter.set(statusFilter);
    this.pageNumber.set(1);
    this.loadItems();
  }

  onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.pageNumber.set(1);
    this.loadItems();
  }

  onPageNumberChange(pageNumber: number): void {
    this.pageNumber.set(pageNumber);
    this.loadItems();
  }

  onTablePageChange(event: { page: number; rows: number }): void {
    this.pageNumber.set(event.page);
    this.pageSize.set(event.rows);
    this.loadItems();
  }

  onCatalogSelectionChange(items: readonly Item[]): void {
    this.selectedCatalogItems.set(items ?? []);
  }

  onPrintSelectedLabels(): void {
    if (!this.canManageInventory()) {
      return;
    }
    this.openPrintDialog(this.selectedCatalogItems());
  }

  onPrintLabel(item: Item): void {
    if (!this.canManageInventory()) {
      return;
    }
    this.openPrintDialog([item]);
  }

  onClosePrintDialog(): void {
    this.printDialogVisible.set(false);
    this.printDialogCandidates.set([]);
  }

  onPrintDialogRequested(request: BarcodeLabelPrintRequest): void {
    this.inventoryService.printBarcodeLabels(request).subscribe({
      next: (response) => {
        const blob = response.body;
        if (blob) {
          try {
            this.blobDownloadService.openPdf(blob);
          } catch {
            this.blobDownloadService.download(blob, 'barcode-labels.pdf');
          }
        }

        this.onClosePrintDialog();
      },
      error: () => {
        this.onClosePrintDialog();
      },
    });
  }

  private openPrintDialog(items: readonly Item[]): void {
    const candidates = (items ?? []).map((item) => ({
      itemId: item.id,
      itemName: item.name,
      barcode: item.barcode,
      inventoryBatchId: null,
    }));

    this.printDialogCandidates.set(candidates);
    this.printDialogVisible.set(true);
  }
}
