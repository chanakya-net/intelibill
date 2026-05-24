import { Component, ViewEncapsulation, computed, effect, inject, signal } from '@angular/core';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { AudioService } from '../../../core/services/audio.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryInboundDraftRow } from '../../../core/storage/inventory-draft-indexeddb.service';
import {
  AddInventoryBatchFailedRow,
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  AddInventoryBatchSucceededRow,
} from '../services/inventory.models';
import { InventoryService } from '../services/inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { BatchDraftStateService } from '../services/batch-draft-state.service';
import { BatchRowFormStateService } from '../services/batch-row-form-state.service';
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
  private readonly authService = inject(AuthService);
  private readonly audioService = inject(AudioService);
  private readonly inventoryService = inject(InventoryService);
  private readonly draftState = inject(BatchDraftStateService);
  private readonly rowFormState = inject(BatchRowFormStateService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);
  private readonly catalogSync = inject(ProductCatalogSyncService);

  readonly isSaving = signal(false);
  readonly isScannerOpen = signal(false);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly scannerLastAction = signal('');
  readonly scannerSessionCount = signal(0);
  readonly highlightedRowId = signal<string | null>(null);
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly pendingRows = this.draftState.pendingRows;
  readonly loadingDraft = this.draftState.loadingDraft;
  readonly form = this.rowFormState.form;
  readonly loadingProduct = this.rowFormState.loadingProduct;
  readonly isLoadingHsn = this.rowFormState.isLoadingHsn;
  readonly selectedHsnCode = this.rowFormState.selectedHsnCode;
  readonly hsnResult = this.rowFormState.hsnResult;
  readonly pickerOpen = this.rowFormState.pickerOpen;
  readonly nameSuggestions = this.rowFormState.nameSuggestions;
  readonly barcodeSuggestions = this.rowFormState.barcodeSuggestions;
  readonly supplierSuggestions = this.rowFormState.supplierSuggestions;
  readonly taxModeOptions = this.rowFormState.taxModeOptions;
  readonly filteredPickerHsnOptions = this.rowFormState.filteredPickerHsnOptions;
  readonly filteredPickerTaxOptions = this.rowFormState.filteredPickerTaxOptions;
  readonly suppliers = this.rowFormState.suppliers;

  constructor() {
    effect(() => {
      const shopId = this.activeShopId();
      if (!shopId) {
        void this.draftState.clearRows('');
        return;
      }

      this.rowFormState.resetForm();
      this.suppliersFacadeLoad();
      void this.loadDraftRows(shopId);
    });
  }

  onFilterName(event: AutoCompleteCompleteEvent): void {
    this.rowFormState.onFilterName(event);
  }

  onFilterBarcode(event: AutoCompleteCompleteEvent): void {
    this.rowFormState.onFilterBarcode(event);
  }

  onNameSelected(name: string): void {
    this.rowFormState.onNameSelected(name);
  }

  onBarcodeSelected(barcode: string): void {
    this.rowFormState.onBarcodeSelected(barcode);
  }

  onBarcodeFocusOut(): void {
    this.rowFormState.onBarcodeFocusOut();
  }

  async onItemNameBlur(): Promise<void> {
    await this.rowFormState.onItemNameBlur();
  }

  async fetchProductDetails(
    options?: { showInfoToast?: boolean; showErrorToast?: boolean; lookupHsnIfMissing?: boolean },
  ): Promise<void> {
    const result = await this.rowFormState.fetchProductDetails({
      lookupHsnIfMissing: options?.lookupHsnIfMissing,
    });

    if (result.patched && options?.showInfoToast !== false) {
      this.showInfo('inventory.productDetailsLoaded');
    }

    if (result.error && options?.showErrorToast !== false) {
      this.showError('inventory.productDetailsLoadError');
    }
  }

  applyHsnSelection(hsnCode: string, taxPercentage: string): void {
    this.rowFormState.applyHsnSelection(hsnCode, taxPercentage);
  }

  dismissPicker(): void {
    this.rowFormState.dismissPicker();
  }

  clearHsnSelection(): void {
    this.rowFormState.clearHsnSelection();
  }

  async onChangeHsnClick(): Promise<void> {
    await this.rowFormState.onChangeHsnClick();
  }

  filterPickerHsn(event: AutoCompleteCompleteEvent): void {
    this.rowFormState.filterPickerHsn(event);
  }

  filterPickerTax(event: AutoCompleteCompleteEvent): void {
    this.rowFormState.filterPickerTax(event);
  }

  onFilterSupplier(event: AutoCompleteCompleteEvent): void {
    this.rowFormState.onFilterSupplier(event);
  }

  onSupplierSelected(selection: string): void {
    this.rowFormState.onSupplierSelected(selection);
  }

  onAddRow(): void {
    const row = this.rowFormState.buildDraftRow();
    if (!row) {
      this.rowFormState.form.markAllAsTouched();
      return;
    }

    void this.persistDraftRow(row);
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
    if (!row) {
      return;
    }

    this.saveSummary.set(null);
    this.rowFormState.loadDraftRow(row);
    void this.draftState.removeRow(this.activeShopId(), clientRowId);
  }

  onClearAll(): void {
    this.saveSummary.set(null);
    void this.draftState.clearRows(this.activeShopId());
  }

  onSaveAll(): void {
    if (this.isSaving() || this.pendingRows().length === 0) {
      return;
    }

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
    if (!barcode) {
      return;
    }

    this.scannerSessionCount.update((count) => count + 1);

    const mergedRow = await this.draftState.incrementRowQuantity(this.activeShopId(), barcode);
    if (mergedRow) {
      this.flashRow(mergedRow.clientRowId);
      this.scannerLastAction.set('inventory.scannerActionIncremented');
      this.showScannerToast('inventory.scannerMerged', barcode, mergedRow.quantity);
      void this.audioService.beep();
      return;
    }

    const catalogEntry = this.catalogSync.findByBarcode(barcode);
    this.rowFormState.form.controls.barcode.setValue(barcode);
    this.rowFormState.form.controls.barcode.markAsDirty();

    if (catalogEntry) {
      this.rowFormState.form.controls.itemName.setValue(catalogEntry.name);
    }

    await this.fetchProductDetails({ showInfoToast: false, showErrorToast: false });

    if (this.rowFormState.canAutoAddScannedRow()) {
      const row = this.rowFormState.buildDraftRow();
      if (row) {
        await this.persistDraftRow(row);
        this.flashRow(row.clientRowId);
        this.scannerLastAction.set('inventory.scannerActionAdded');
        this.showScannerToast('inventory.scannerAdded', barcode, 1);
        void this.audioService.beep();
        return;
      }
    }

    this.rowFormState.form.markAllAsTouched();
    this.scannerLastAction.set('inventory.scannerActionReview');
    this.showWarn('inventory.scannerNeedsReview');
    this.isScannerOpen.set(false);
  }

  getSupplierDisplayName(supplierId: string | null): string {
    return this.rowFormState.getSupplierDisplayName(supplierId);
  }

  private async loadDraftRows(shopId: string): Promise<void> {
    const rows = await this.draftState.loadDraftRows(shopId);
    if (rows.length > 0) {
      this.showInfo('inventory.draftLoaded');
    }
  }

  private async persistDraftRow(row: InventoryInboundDraftRow): Promise<void> {
    const shopId = this.activeShopId();
    if (!shopId) {
      return;
    }

    this.saveSummary.set(null);
    await this.draftState.addRow(shopId, row);
    this.rowFormState.resetForm();
  }

  private async saveRowsInChunks(): Promise<void> {
    const rows = [...this.pendingRows()];
    const chunkSize = 100;
    const succeeded: AddInventoryBatchSucceededRow[] = [];
    const failed: AddInventoryBatchFailedRow[] = [];

    try {
      for (let index = 0; index < rows.length; index += chunkSize) {
        const chunk = rows.slice(index, index + chunkSize);
        const payload = this.mapRowsToRequest(chunk);
        const response = await firstValueFrom(this.inventoryService.addInventoryBatch({ items: payload }));
        succeeded.push(...response.succeeded);
        failed.push(...response.failed);
      }

      const summary: AddInventoryBatchResponse = {
        requestedCount: rows.length,
        successCount: succeeded.length,
        failedCount: failed.length,
        succeeded,
        failed,
      };

      this.saveSummary.set(summary);
      if (summary.failedCount === 0) {
        this.showSuccess('inventory.savedSuccess', summary.successCount, summary.requestedCount);
        await this.draftState.clearRows(this.activeShopId());
        return;
      }

      const failedIds = new Set(summary.failed.map((row) => row.clientRowId));
      const remainingRows = rows.filter((row) => failedIds.has(row.clientRowId));
      await this.draftState.savePendingRows(this.activeShopId(), remainingRows);
      this.showWarn('inventory.savedPartial');
    } catch (error) {
      const succeededIds = new Set(succeeded.map((row) => row.clientRowId));
      const failedIds = new Set(failed.map((row) => row.clientRowId));
      const remainingRows = rows.filter(
        (row) => !succeededIds.has(row.clientRowId) || failedIds.has(row.clientRowId),
      );

      await this.draftState.savePendingRows(this.activeShopId(), remainingRows);

      if (succeeded.length > 0 || failed.length > 0) {
        this.saveSummary.set({
          requestedCount: rows.length,
          successCount: succeeded.length,
          failedCount: failed.length,
          succeeded,
          failed,
        });
      }

      const apiErrors = this.extractApiErrors(error);
      const hasBatchLimitError = apiErrors.some((apiError) => this.getErrorCode(apiError) === 'Inventory.BatchLimitExceeded');

      if (hasBatchLimitError) {
        this.showWarn('inventory.batchLimitReached');
      } else {
        const firstDetail = apiErrors.map((apiError) => this.getErrorDescription(apiError)).find((detail) => detail.length > 0);
        if (firstDetail) {
          this.showErrorWithDetail('inventory.saveFailed', firstDetail);
        } else {
          this.showError('inventory.saveFailed');
        }
      }
    } finally {
      this.isSaving.set(false);
    }
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

  private suppliersFacadeLoad(): void {
    this.suppliersFacade.load();
  }

  private showInfo(messageKey: string): void {
    this.messageService.add({
      severity: 'info',
      summary: this.translate(messageKey),
      life: 2500,
    });
  }

  private showWarn(messageKey: string): void {
    this.messageService.add({
      severity: 'warn',
      summary: this.translate(messageKey),
      life: 3000,
    });
  }

  private showError(messageKey: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate(messageKey),
      life: 3500,
    });
  }

  private showErrorWithDetail(messageKey: string, detail: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate(messageKey),
      detail,
      life: 3500,
    });
  }

  private showSuccess(messageKey: string, successCount: number, requestedCount: number): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate(messageKey),
      detail: `${successCount}/${requestedCount}`,
      life: 3000,
    });
  }

  private showScannerToast(summaryKey: string, barcode: string, quantity: number): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate(summaryKey),
      detail: `${barcode} • Qty ${quantity}`,
      life: 2200,
    });
  }

  private extractApiErrors(error: unknown): readonly Record<string, unknown>[] {
    if (!error || typeof error !== 'object') {
      return [];
    }

    const candidate = (error as { error?: unknown }).error;
    if (!candidate || typeof candidate !== 'object') {
      return [];
    }

    const rawErrors = (candidate as { errors?: unknown }).errors;
    if (!Array.isArray(rawErrors)) {
      return [];
    }

    return rawErrors.filter((entry): entry is Record<string, unknown> => !!entry && typeof entry === 'object');
  }

  private getErrorCode(apiError: Record<string, unknown>): string {
    const code = apiError['code'] ?? apiError['Code'];
    return typeof code === 'string' ? code : '';
  }

  private getErrorDescription(apiError: Record<string, unknown>): string {
    const description = apiError['description'] ?? apiError['Description'];
    return typeof description === 'string' ? description : '';
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }

  private flashRow(clientRowId: string): void {
    this.highlightedRowId.set(clientRowId);
    if (this.highlightTimer) {
      clearTimeout(this.highlightTimer);
    }

    this.highlightTimer = setTimeout(() => {
      this.highlightedRowId.set(null);
      this.highlightTimer = null;
    }, 1600);
  }
}
