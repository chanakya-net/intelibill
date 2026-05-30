import { Component, ViewChild, ViewEncapsulation, computed, effect, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { AudioService } from '../../../core/services/audio.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { InventoryInboundDraftRow } from '../../../core/storage/inventory-draft-indexeddb.service';
import {
  AddInventoryBatchFailedRow,
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  AddInventoryBatchSucceededRow,
} from '../services/inventory.models';
import { BatchDraftStateService } from '../services/batch-draft-state.service';
import { BatchRowFormStateService } from '../services/batch-row-form-state.service';
import { InventoryService } from '../services/inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { BatchRowFormComponent } from '../components/batch-page/batch-row-form.component';
import { BatchSaveResultsComponent } from '../components/batch-page/batch-save-results.component';

@Component({
  selector: 'app-inventory-batch-page',
  standalone: true,
  imports: [
    ToastModule,
    TranslocoPipe,
    BarcodeScannerDialogComponent,
    BatchRowFormComponent,
    BatchSaveResultsComponent,
  ],
  providers: [MessageService],
  templateUrl: './inventory-batch-page.component.html',
  styleUrl: './inventory-batch-page.component.scss',
  encapsulation: ViewEncapsulation.None,
})
export class InventoryBatchPageComponent {
  @ViewChild(BatchRowFormComponent) batchRowForm!: BatchRowFormComponent;

  private readonly authService = inject(AuthService);
  private readonly audioService = inject(AudioService);
  private readonly draftState = inject(BatchDraftStateService);
  private readonly inventoryService = inject(InventoryService);
  private readonly messageService = inject(MessageService);
  private readonly rowFormState = inject(BatchRowFormStateService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly translocoService = inject(TranslocoService);

  readonly isSaving = signal(false);
  readonly isScannerOpen = signal(false);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly scannerLastAction = signal('');
  readonly scannerSessionCount = signal(0);
  readonly highlightedRowId = signal<string | null>(null);

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly loadingDraft = this.draftState.loadingDraft;
  readonly pendingRows = this.draftState.pendingRows;

  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const shopId = this.activeShopId();
      if (!shopId) {
        void this.draftState.clearRows('');
        return;
      }

      this.rowFormState.resetForm();
      this.suppliersFacade.load();
      void this.loadDraftRows(shopId);
    });
  }

  onRowSubmitted(row: InventoryInboundDraftRow): void {
    void this.persistDraftRow(row);
  }

  onRemoveRow(clientRowId: string): void {
    this.saveSummary.set(null);
    void this.draftState.removeRow(this.activeShopId(), clientRowId);
  }

  onEditRow(clientRowId: string): void {
    const row = this.pendingRows().find((candidate) => candidate.clientRowId === clientRowId);
    if (!row) return;

    this.saveSummary.set(null);
    this.rowFormState.loadDraftRow(row);
    void this.draftState.removeRow(this.activeShopId(), clientRowId);
  }

  onClearAll(): void {
    this.saveSummary.set(null);
    void this.draftState.clearRows(this.activeShopId());
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

    this.scannerSessionCount.update((count) => count + 1);
    const mergedRow = await this.draftState.incrementRowQuantity(this.activeShopId(), barcode);
    if (mergedRow) {
      this.saveSummary.set(null);
      this.flashRow(mergedRow.clientRowId);
      this.scannerLastAction.set('inventory.scannerActionIncremented');
      this.showScannerToast('inventory.scannerMerged', barcode, mergedRow.quantity);
      void this.audioService.beep();
      return;
    }

    const scanResult = await this.rowFormState.prepareScannedRow(barcode);
    if (scanResult.status === 'added') {
      await this.persistDraftRow(scanResult.row);
      this.flashRow(scanResult.row.clientRowId);
      this.scannerLastAction.set('inventory.scannerActionAdded');
      this.showScannerToast('inventory.scannerAdded', barcode, 1);
      void this.audioService.beep();
      return;
    }

    this.scannerLastAction.set('inventory.scannerActionReview');
    this.showWarn('inventory.scannerNeedsReview');
    if (scanResult.status === 'missingPricing') {
      this.batchRowForm.showPricingReviewRequired();
    }
    this.isScannerOpen.set(false);
  }

  private async loadDraftRows(shopId: string): Promise<void> {
    const rows = await this.draftState.loadDraftRows(shopId);
    if (rows.length > 0) this.showInfo('inventory.draftLoaded');
  }

  private async persistDraftRow(row: InventoryInboundDraftRow): Promise<void> {
    const shopId = this.activeShopId();
    if (!shopId) return;

    this.saveSummary.set(null);
    await this.draftState.addRow(shopId, row);
    this.rowFormState.resetForm();
  }

  private async saveRowsInChunks(): Promise<void> {
    const rows = [...this.pendingRows()];
    const succeeded: AddInventoryBatchSucceededRow[] = [];
    const failed: AddInventoryBatchFailedRow[] = [];

    try {
      for (let index = 0; index < rows.length; index += 100) {
        const chunk = rows.slice(index, index + 100);
        const response = await firstValueFrom(
          this.inventoryService.addInventoryBatch({ items: this.mapRowsToRequest(chunk) }),
        );
        succeeded.push(...response.succeeded);
        failed.push(...response.failed);
      }

      const summary = this.buildSaveSummary(rows.length, succeeded, failed);
      this.saveSummary.set(summary);
      if (summary.failedCount === 0) {
        this.showSuccess('inventory.savedSuccess', summary.successCount, summary.requestedCount);
        await this.draftState.clearRows(this.activeShopId());
        return;
      }

      await this.keepRows(rows, new Set(summary.failed.map((row) => row.clientRowId)));
      this.showWarn('inventory.savedPartial');
    } catch (error) {
      const succeededIds = new Set(succeeded.map((row) => row.clientRowId));
      const failedIds = new Set(failed.map((row) => row.clientRowId));
      await this.keepRows(rows, new Set(rows.filter((row) => !succeededIds.has(row.clientRowId) || failedIds.has(row.clientRowId)).map((row) => row.clientRowId)));

      if (succeeded.length > 0 || failed.length > 0) {
        this.saveSummary.set(this.buildSaveSummary(rows.length, succeeded, failed));
      }

      const apiErrors = this.extractApiErrors(error);
      if (apiErrors.some((apiError) => this.getErrorCode(apiError) === 'Inventory.BatchLimitExceeded')) {
        this.showWarn('inventory.batchLimitReached');
      } else {
        const detail = apiErrors.map((apiError) => this.getErrorDescription(apiError)).find(Boolean);
        detail ? this.showErrorWithDetail('inventory.saveFailed', detail) : this.showError('inventory.saveFailed');
      }
    } finally {
      this.isSaving.set(false);
    }
  }

  private async keepRows(rows: readonly InventoryInboundDraftRow[], keepIds: ReadonlySet<string>): Promise<void> {
    await this.draftState.savePendingRows(this.activeShopId(), rows.filter((row) => keepIds.has(row.clientRowId)));
  }

  private buildSaveSummary(
    requestedCount: number,
    succeeded: readonly AddInventoryBatchSucceededRow[],
    failed: readonly AddInventoryBatchFailedRow[],
  ): AddInventoryBatchResponse {
    return { requestedCount, successCount: succeeded.length, failedCount: failed.length, succeeded, failed };
  }

  private mapRowsToRequest(rows: readonly InventoryInboundDraftRow[]): readonly AddInventoryBatchRowRequest[] {
    return rows.map((row) => ({
      clientRowId: row.clientRowId,
      itemName: row.itemName,
      barcode: row.barcode,
      itemDescription: row.itemDescription,
      hsnCode: row.hsnCode,
      uom: row.uom,
      batchNumber: row.batchNumber,
      quantity: row.quantity,
      totalPurchaseCost: row.totalPurchaseCost,
      mrp: row.mrp,
      salesPrice: row.salesPrice,
      taxRatePercent: row.taxRatePercent,
      taxIncluded: row.taxIncluded,
      purchaseTaxIncluded: row.taxIncluded,
      expiryDate: row.expiryDate,
      manufacturingDate: row.manufacturingDate,
      supplierId: row.supplierId,
      referenceNumber: row.referenceNumber,
      notes: row.notes,
      performedAt: new Date().toISOString(),
    }));
  }

  private showInfo(key: string): void { this.notify('info', key, 2500); }

  private showWarn(key: string): void { this.notify('warn', key, 3000); }

  private showError(key: string): void { this.notify('error', key, 3500); }

  private showErrorWithDetail(key: string, detail: string): void { this.notify('error', key, 3500, detail); }

  private showSuccess(key: string, successCount: number, requestedCount: number): void {
    this.notify('success', key, 3000, `${successCount}/${requestedCount}`);
  }

  private showScannerToast(key: string, barcode: string, quantity: number): void {
    this.notify('success', key, 2200, `${barcode} • Qty ${quantity}`);
  }

  private notify(severity: 'info' | 'success' | 'warn' | 'error', key: string, life: number, detail?: string): void {
    this.messageService.add({ severity, summary: this.translocoService.translate(key), detail, life });
  }

  private extractApiErrors(error: unknown): readonly Record<string, unknown>[] {
    if (!error || typeof error !== 'object') return [];
    const candidate = (error as { error?: unknown }).error;
    if (!candidate || typeof candidate !== 'object') return [];
    const rawErrors = (candidate as { errors?: unknown }).errors;
    return Array.isArray(rawErrors)
      ? rawErrors.filter((entry): entry is Record<string, unknown> => !!entry && typeof entry === 'object')
      : [];
  }

  private getErrorCode(apiError: Record<string, unknown>): string {
    const code = apiError['code'] ?? apiError['Code'];
    return typeof code === 'string' ? code : '';
  }

  private getErrorDescription(apiError: Record<string, unknown>): string {
    const description = apiError['description'] ?? apiError['Description'];
    return typeof description === 'string' ? description : '';
  }

  private flashRow(clientRowId: string): void {
    this.highlightedRowId.set(clientRowId);
    if (this.highlightTimer) clearTimeout(this.highlightTimer);
    this.highlightTimer = setTimeout(() => {
      this.highlightedRowId.set(null);
      this.highlightTimer = null;
    }, 1600);
  }
}
