import { Component, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';

import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import {
  AddInventoryBatchFailedRow,
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  AddInventoryBatchSucceededRow,
  InventoryService,
} from '../services/inventory.service';
import { InventoryInboundDraftRow } from '../../../core/storage/inventory-draft-indexeddb.service';
import { AudioService } from '../../../core/services/audio.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { BatchDraftStateService } from '../services/batch-draft-state.service';
import { BatchRowFormComponent } from '../components/batch-page/batch-row-form.component';
import { BatchSaveResultsComponent } from '../components/batch-page/batch-save-results.component';

@Component({
  selector: 'app-inventory-batch-page',
  standalone: true,
  imports: [
    TranslocoPipe,
    BarcodeScannerDialogComponent,
    BatchRowFormComponent,
    BatchSaveResultsComponent,
    ButtonModule,
    TableModule,
    ToastModule,
    ProgressSpinnerModule,
  ],
  providers: [MessageService],
  templateUrl: './inventory-batch-page.component.html',
  styleUrl: './inventory-batch-page.component.scss',
})
export class InventoryBatchPageComponent {
  @ViewChild(BatchRowFormComponent) batchRowForm!: BatchRowFormComponent;

  private readonly draftState = inject(BatchDraftStateService);
  private readonly inventoryService = inject(InventoryService);
  private readonly authService = inject(AuthService);
  private readonly audioService = inject(AudioService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);
  readonly catalogSync = inject(ProductCatalogSyncService);

  readonly isSaving = signal(false);
  readonly isScannerOpen = signal(false);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly highlightedRowId = signal<string | null>(null);
  readonly scannerLastAction = signal('');
  readonly scannerSessionCount = signal(0);

  readonly pendingRows = this.draftState.pendingRows;
  readonly loadingDraft = this.draftState.loadingDraft;
  readonly suppliers = this.suppliersFacade.suppliers;
  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly tableRows = computed(() => [...this.pendingRows()]);
  readonly failedClientRowIds = computed(() => {
    const s = this.saveSummary();
    return s ? new Set(s.failed.map((r) => r.clientRowId)) : new Set<string>();
  });
  readonly failedRowErrorTextById = computed(() => {
    const s = this.saveSummary();
    if (!s) return new Map<string, string>();
    return new Map(s.failed.map((r) => [r.clientRowId, r.errors.map((e) => e.description).join(', ')] as const));
  });

  readonly editButtonPt = { root: { class: 'edit-row-button' } };
  readonly removeButtonPt = { root: { class: 'remove-row-button' } };

  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const shopId = this.activeShopId();
      if (!shopId) {
        this.draftState.pendingRows.set([]);
        return;
      }
      this.suppliersFacade.load();
      void this.draftState.loadDraft(shopId).then(() => {
        if (this.draftState.pendingRows().length > 0) {
          this.showInfo('inventory.draftLoaded');
        }
      });
    });
  }

  onRowAdded(row: InventoryInboundDraftRow): void {
    this.saveSummary.set(null);
    void this.draftState.saveDraftRow(row);
    this.flashRow(row.clientRowId);
  }

  onRemoveRow(clientRowId: string): void {
    this.saveSummary.set(null);
    void this.draftState.removeDraftRow(clientRowId);
  }

  onEditRow(clientRowId: string): void {
    const row = this.pendingRows().find((r) => r.clientRowId === clientRowId);
    if (!row) return;
    this.saveSummary.set(null);
    void this.draftState.removeDraftRow(clientRowId);
    this.batchRowForm?.populateFromRow(row, this.getSupplierDisplayName(row.supplierId));
  }

  onClearAll(): void {
    this.saveSummary.set(null);
    void this.draftState.clearDraft(this.activeShopId());
  }

  onSaveAll(): void {
    if (this.isSaving() || this.pendingRows().length === 0) return;
    this.isSaving.set(true);
    void this.saveRowsInChunks();
  }

  openScanner(): void {
    this.scannerLastAction.set('');
    this.scannerSessionCount.set(0);
    this.isScannerOpen.set(true);
    void this.audioService.prime();
  }

  onScannerVisibilityChange(visible: boolean): void {
    this.isScannerOpen.set(visible);
  }

  async handleScannedBarcode(detection: BarcodeDetection): Promise<void> {
    const barcode = detection.value.trim();
    if (!barcode) return;

    this.scannerSessionCount.update((c) => c + 1);

    const mergedRow = await this.draftState.incrementRowQuantity(barcode);
    if (mergedRow) {
      this.saveSummary.set(null);
      this.flashRow(mergedRow.clientRowId);
      this.scannerLastAction.set('inventory.scannerActionIncremented');
      this.showScannerToast('inventory.scannerMerged', barcode, mergedRow.quantity);
      void this.audioService.beep();
      return;
    }

    const result = await this.batchRowForm?.handleBarcode(barcode);
    if (result === 'added') {
      this.scannerLastAction.set('inventory.scannerActionAdded');
      this.showScannerToast('inventory.scannerAdded', barcode, 1);
      void this.audioService.beep();
      return;
    }

    this.scannerLastAction.set('inventory.scannerActionReview');
    this.showWarn('inventory.scannerNeedsReview');
    this.isScannerOpen.set(false);
  }

  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) return '-';
    return this.suppliers().find((s) => s.supplierId === supplierId)?.name ?? supplierId;
  }

  pendingRowInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  pendingRowAvatarColor(name: string): string {
    const colors = ['#b45309', '#0369a1', '#15803d', '#7c3aed', '#be185d', '#c2410c', '#0f766e', '#1d4ed8'];
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
  }

  private async saveRowsInChunks(): Promise<void> {
    const rows = [...this.pendingRows()];
    const chunkSize = 100;
    const succeeded: AddInventoryBatchSucceededRow[] = [];
    const failed: AddInventoryBatchFailedRow[] = [];
    try {
      for (let i = 0; i < rows.length; i += chunkSize) {
        const chunk = rows.slice(i, i + chunkSize);
        const response = await firstValueFrom(
          this.inventoryService.addInventoryBatch({ items: this.mapRowsToRequest(chunk) }),
        );
        succeeded.push(...response.succeeded);
        failed.push(...response.failed);
      }
      const summary: AddInventoryBatchResponse = {
        requestedCount: rows.length, successCount: succeeded.length,
        failedCount: failed.length, succeeded, failed,
      };
      this.saveSummary.set(summary);
      if (summary.failedCount === 0) {
        await this.draftState.clearDraft(this.activeShopId());
        this.showSuccess('inventory.savedSuccess', summary.successCount, summary.requestedCount);
        return;
      }
      const failedIds = new Set(summary.failed.map((r) => r.clientRowId));
      await this.draftState.replaceRows(this.activeShopId(), rows.filter((r) => failedIds.has(r.clientRowId)));
      this.showWarn('inventory.savedPartial');
    } catch (error) {
      const succeededIds = new Set(succeeded.map((r) => r.clientRowId));
      const failedIds = new Set(failed.map((r) => r.clientRowId));
      await this.draftState.replaceRows(
        this.activeShopId(),
        rows.filter((r) => !succeededIds.has(r.clientRowId) || failedIds.has(r.clientRowId)),
      );
      if (succeeded.length > 0 || failed.length > 0) {
        this.saveSummary.set({ requestedCount: rows.length, successCount: succeeded.length, failedCount: failed.length, succeeded, failed });
      }
      const apiErrors = this.extractApiErrors(error);
      if (apiErrors.some((e) => e['code'] === 'Inventory.BatchLimitExceeded' || e['Code'] === 'Inventory.BatchLimitExceeded')) {
        this.showWarn('inventory.batchLimitReached');
      } else {
        const detail = apiErrors.map((e) => String(e['description'] ?? e['Description'] ?? '')).find((d) => d.length > 0);
        detail ? this.showErrorWithDetail('inventory.saveFailed', detail) : this.showError('inventory.saveFailed');
      }
    } finally {
      this.isSaving.set(false);
    }
  }

  private mapRowsToRequest(rows: readonly InventoryInboundDraftRow[]): readonly AddInventoryBatchRowRequest[] {
    return rows.map((r) => ({
      clientRowId: r.clientRowId, itemName: r.itemName, barcode: r.barcode,
      itemDescription: r.itemDescription, hsnCode: r.hsnCode, uom: r.uom,
      batchNumber: r.batchNumber, quantity: r.quantity, totalPurchaseCost: r.totalPurchaseCost,
      mrp: r.mrp, salesPrice: r.salesPrice, taxRatePercent: r.taxRatePercent,
      taxIncluded: r.taxIncluded, purchaseTaxIncluded: r.taxIncluded,
      expiryDate: r.expiryDate, manufacturingDate: r.manufacturingDate,
      supplierId: r.supplierId, referenceNumber: r.referenceNumber,
      notes: r.notes, performedAt: new Date().toISOString(),
    }));
  }

  private flashRow(clientRowId: string): void {
    this.highlightedRowId.set(clientRowId);
    if (this.highlightTimer) clearTimeout(this.highlightTimer);
    this.highlightTimer = setTimeout(() => { this.highlightedRowId.set(null); this.highlightTimer = null; }, 1600);
  }

  private extractApiErrors(error: unknown): readonly Record<string, unknown>[] {
    const errors = (error as { error?: { errors?: unknown[] } })?.error?.errors;
    return Array.isArray(errors) ? errors.filter((e): e is Record<string, unknown> => !!e && typeof e === 'object') : [];
  }

  private showInfo(key: string): void { this.messageService.add({ severity: 'info', summary: this.translocoService.translate(key), life: 2500 }); }
  private showSuccess(key: string, s: number, r: number): void { this.messageService.add({ severity: 'success', summary: this.translocoService.translate(key), detail: `${s}/${r}`, life: 3000 }); }
  private showWarn(key: string): void { this.messageService.add({ severity: 'warn', summary: this.translocoService.translate(key), life: 3000 }); }
  private showError(key: string): void { this.messageService.add({ severity: 'error', summary: this.translocoService.translate(key), life: 3500 }); }
  private showErrorWithDetail(key: string, detail: string): void { this.messageService.add({ severity: 'error', summary: this.translocoService.translate(key), detail, life: 3500 }); }
  private showScannerToast(key: string, barcode: string, qty: number): void { this.messageService.add({ severity: 'success', summary: this.translocoService.translate(key), detail: `${barcode} • Qty ${qty}`, life: 2200 }); }
}
