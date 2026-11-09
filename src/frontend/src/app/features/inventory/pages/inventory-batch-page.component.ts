import { ExpiryPolicyService } from '../../expiry/services/expiry-policy.service';
import {
  Component,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { StaleScopeError, isScopeChangeError } from '../../../core/context/scoped-request';
import { hasProblemCode } from '../../../shared/utils/problem-code.util';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import { AudioService } from '../../../core/services/audio.service';
import { classifyFinancialOperationFailure } from '../../../core/services/financial-operation-intent.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import {
  InventoryInboundDraftRow,
  isPurchaseTaxModeConfirmed,
} from '../../../core/storage/inventory-draft-indexeddb.service';
import {
  BarcodeLabelPrintDialogComponent,
  type BarcodeLabelPrintCandidate,
} from '../components/barcode-label-print-dialog.component';
import {
  AddInventoryBatchFailedRow,
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  AddInventoryBatchSucceededRow,
  type BarcodeLabelPrintRequest,
} from '../services/inventory.models';
import { BatchDraftStateService } from '../services/batch-draft-state.service';
import { BatchRowFormStateService } from '../services/batch-row-form-state.service';
import { InventoryService } from '../services/inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { downloadBlob, openPdfBlobInNewTab } from '../../../shared/utils/blob-download.util';
import { BatchRowFormComponent } from '../components/batch-page/batch-row-form.component';
import { BatchSaveResultsComponent } from '../components/batch-page/batch-save-results.component';

@Component({
  selector: 'app-inventory-batch-page',
  standalone: true,
  imports: [
    ButtonModule,
    ToastModule,
    TranslocoPipe,
    BarcodeScannerDialogComponent,
    BarcodeLabelPrintDialogComponent,
    BatchRowFormComponent,
    BatchSaveResultsComponent,
  ],
  providers: [MessageService, BatchRowFormStateService],
  templateUrl: './inventory-batch-page.component.html',
  styleUrl: './inventory-batch-page.component.scss',
  encapsulation: ViewEncapsulation.None,
})
export class InventoryBatchPageComponent {
  @ViewChild(BatchRowFormComponent) batchRowForm!: BatchRowFormComponent;

  private readonly expiryPolicies = inject(ExpiryPolicyService);
  private readonly authService = inject(AuthService);
  private readonly shopContext = inject(ShopContextService);
  private readonly audioService = inject(AudioService);
  private readonly draftState = inject(BatchDraftStateService);
  private readonly inventoryService = inject(InventoryService);
  private readonly messageService = inject(MessageService);
  private readonly rowFormState = inject(BatchRowFormStateService);
  private readonly translocoService = inject(TranslocoService);

  readonly isSaving = signal(false);
  readonly isScannerOpen = signal(false);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly barcodeLabelPrintDialogVisible = signal(false);
  readonly barcodeLabelPrintCandidates = signal<readonly BarcodeLabelPrintCandidate[]>([]);
  readonly scannerLastAction = signal('');
  readonly scannerSessionCount = signal(0);
  readonly highlightedRowId = signal<string | null>(null);

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly loadingDraft = this.draftState.loadingDraft;
  readonly pendingRows = this.draftState.pendingRows;
  readonly hasInterruptedSubmission = this.draftState.hasInterruptedSubmission;

  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const shopId = this.activeShopId();
      if (!shopId) {
        void this.draftState.clearRows('');
        return;
      }

      this.rowFormState.resetForm();
      void this.loadDraftRows(shopId);
    });
  }

  onRowSubmitted(row: InventoryInboundDraftRow): void {
    if (this.refuseWhileInterrupted()) return;
    void this.persistDraftRow(row);
  }

  onRemoveRow(clientRowId: string): void {
    if (this.refuseWhileInterrupted()) return;
    this.saveSummary.set(null);
    void this.draftState.removeRow(this.activeShopId(), clientRowId);
  }

  onEditRow(clientRowId: string): void {
    if (this.refuseWhileInterrupted()) return;
    const row = this.pendingRows().find((candidate) => candidate.clientRowId === clientRowId);
    if (!row) return;

    this.saveSummary.set(null);
    this.batchRowForm.populateFromRow(row);
    void this.draftState.removeRow(this.activeShopId(), clientRowId);
  }

  onClearAll(): void {
    if (this.refuseWhileInterrupted()) return;
    this.saveSummary.set(null);
    void this.draftState.clearRows(this.activeShopId());
  }

  onSaveAll(): void {
    // An interrupted submission can be resent even when no draft rows remain on screen.
    if (this.isSaving() || (this.pendingRows().length === 0 && !this.hasInterruptedSubmission()))
      return;
    // An interrupted submission resends its frozen rows unchanged, so it is never held back here.
    if (
      !this.hasInterruptedSubmission() &&
      this.pendingRows().some((row) => !isPurchaseTaxModeConfirmed(row))
    ) {
      // Drafts kept by earlier releases carry no purchase mode or one copied from the sales mode:
      // the user confirms it per row (edit the row) before anything is sent.
      this.showWarn('inventory.purchaseTaxModeMissingRows');
      return;
    }

    this.isSaving.set(true);
    void this.saveRowsInChunks();
  }

  onPrintSuccessfulRows(rows: readonly AddInventoryBatchSucceededRow[]): void {
    this.openBarcodeLabelPrintDialog(this.mapSuccessfulRowsToPrintCandidates(rows));
  }

  onBarcodeLabelPrintRequested(request: BarcodeLabelPrintRequest): void {
    this.inventoryService.printBarcodeLabels(request).subscribe({
      next: (response) => {
        const blob = response.body;
        if (!blob) {
          this.showError('inventory.barcodeLabels.printFailed');
          return;
        }

        this.openOrDownloadLabelBlob(blob);
        this.barcodeLabelPrintDialogVisible.set(false);
        this.barcodeLabelPrintCandidates.set([]);
      },
      error: () => this.showError('inventory.barcodeLabels.printFailed'),
    });
  }

  onBarcodeLabelPrintDialogClosed(): void {
    this.barcodeLabelPrintDialogVisible.set(false);
    this.barcodeLabelPrintCandidates.set([]);
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
    if (this.refuseWhileInterrupted()) return;

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
    // Captured once: every chunk is sent as, and every draft write lands under, the user and shop
    // that started the save, whatever this tab is working in when the answers arrive.
    const owner: ScopeOwner = {
      userId: this.authService.session()?.user?.id ?? '',
      shopId: this.activeShopId(),
    };
    void this.expiryPolicies.invalidate(owner.shopId).catch(() => undefined);
    const succeeded: AddInventoryBatchSucceededRow[] = [];
    const failed: AddInventoryBatchFailedRow[] = [];
    let requestedCount = 0;
    // A switch waits for this save; a move it cannot stop (another tab) leaves the submission stored.
    const mutation = this.shopContext.beginMutation();

    try {
      // Frozen and stored before the first request: if the answer is lost, the next save resends
      // these exact rows under the same keys and the server replays what it already committed.
      const submission = await this.draftState.prepareSubmission(owner.shopId, this.pendingRows());
      requestedCount = submission.chunks.reduce((count, chunk) => count + chunk.rows.length, 0);

      for (const chunk of submission.chunks) {
        this.assertStillIn(owner);

        const response = await firstValueFrom(
          this.inventoryService.addInventoryBatch(
            { items: this.mapRowsToRequest(chunk.rows), operationKey: chunk.operationKey },
            owner,
          ),
        );
        succeeded.push(...response.succeeded);
        failed.push(...response.failed);
      }

      this.assertStillIn(owner);

      // Saved rows leave the draft before the submission is discarded: if this write fails, the
      // submission is still there and a resend only replays what is already committed.
      await this.keepRowsNotSaved(owner.shopId, succeeded);
      const summary = this.buildSaveSummary(requestedCount, succeeded, failed);
      this.saveSummary.set(summary);

      if (failed.some((row) => row.retryable)) {
        // Nothing was saved for those rows; resending the same keys retries only them.
        this.draftState.markSubmissionInterrupted();
        this.showWarn('inventory.saveInterrupted');
        return;
      }

      await this.draftState.clearSubmission(owner.shopId);
      if (summary.failedCount === 0) {
        this.showSuccess('inventory.savedSuccess', summary.successCount, summary.requestedCount);
        return;
      }

      this.showWarn('inventory.savedPartial');
    } catch (error) {
      if (isScopeChangeError(error) || !this.shopContext.isCurrent(owner)) {
        // The submission stays stored under the original shop, with its rows untouched, and
        // resumes as an interrupted save when that shop is active again.
        return;
      }

      await this.keepRowsNotSaved(owner.shopId, succeeded).catch(() => undefined);
      if (succeeded.length > 0 || failed.length > 0) {
        this.saveSummary.set(this.buildSaveSummary(requestedCount, succeeded, failed));
      }

      if (requestedCount > 0 && classifyFinancialOperationFailure(error) === 'uncertain') {
        // The answer may have been lost after rows committed: keep the frozen submission.
        this.draftState.markSubmissionInterrupted();
        this.showWarn('inventory.saveInterrupted');
        return;
      }

      if (requestedCount > 0) {
        // A definitive refusal: the corrected rows will be a new submission with new keys.
        await this.draftState.clearSubmission(owner.shopId);
      }

      const apiErrors = this.extractApiErrors(error);
      if (
        apiErrors.some((apiError) => this.getErrorCode(apiError) === 'Inventory.BatchLimitExceeded')
      ) {
        this.showWarn('inventory.batchLimitReached');
      } else if (hasProblemCode(error, 'Inventory.QuantityPrecisionExceeded')) {
        this.showError('inventory.quantityPrecisionExceeded');
      } else {
        const detail = apiErrors
          .map((apiError) => this.getErrorDescription(apiError))
          .find(Boolean);
        detail
          ? this.showErrorWithDetail('inventory.saveFailed', detail)
          : this.showError('inventory.saveFailed');
      }
    } finally {
      mutation.release();
      this.isSaving.set(false);
    }
  }

  /** Each request carries the token current when it leaves; never send one owner's rows as another. */
  private assertStillIn(owner: ScopeOwner): void {
    if (!this.shopContext.isCurrent(owner)) {
      throw new StaleScopeError();
    }
  }

  /**
   * While a submission is interrupted its rows are frozen: an edit, removal, scan or new row would
   * either be overwritten by the resend or be posted a second time beside it.
   */
  private refuseWhileInterrupted(): boolean {
    if (!this.hasInterruptedSubmission()) return false;
    this.showWarn('inventory.saveInterruptedLocked');
    return true;
  }

  /**
   * Keeps every pending row the server has not confirmed, including rows added while the save ran.
   * Called only while the save's owner is still current, so the rows in memory are that owner's.
   */
  private async keepRowsNotSaved(
    shopId: string,
    succeeded: readonly AddInventoryBatchSucceededRow[],
  ): Promise<void> {
    const savedIds = new Set(succeeded.map((row) => row.clientRowId));
    await this.draftState.savePendingRows(
      shopId,
      this.pendingRows().filter((row) => !savedIds.has(row.clientRowId)),
    );
  }

  private buildSaveSummary(
    requestedCount: number,
    succeeded: readonly AddInventoryBatchSucceededRow[],
    failed: readonly AddInventoryBatchFailedRow[],
  ): AddInventoryBatchResponse {
    return {
      requestedCount,
      successCount: succeeded.length,
      failedCount: failed.length,
      succeeded,
      failed,
    };
  }

  private mapRowsToRequest(
    rows: readonly InventoryInboundDraftRow[],
  ): readonly AddInventoryBatchRowRequest[] {
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
      // Rows are always sent with their own purchase mode. Only a submission frozen by an earlier
      // release can lack it; that release sent the sales mode, so its resend repeats it unchanged.
      // `purchaseTaxModeConfirmed` is draft bookkeeping and is deliberately not sent.
      purchaseTaxIncluded: row.purchaseTaxIncluded ?? row.taxIncluded,
      expiryDate: row.expiryDate,
      manufacturingDate: row.manufacturingDate,
      supplierId: row.supplierId,
      referenceNumber: row.referenceNumber,
      notes: row.notes,
      performedAt: row.performedAt,
    }));
  }

  private mapSuccessfulRowsToPrintCandidates(
    rows: readonly AddInventoryBatchSucceededRow[],
  ): readonly BarcodeLabelPrintCandidate[] {
    return rows.map((row) => ({
      itemId: row.result.itemId,
      itemName: row.result.itemName,
      barcode: row.result.barcode,
      inventoryBatchId: row.result.batchId,
      quantity: row.result.batchQuantity,
    }));
  }

  private openBarcodeLabelPrintDialog(items: readonly BarcodeLabelPrintCandidate[]): void {
    if (items.length === 0) {
      return;
    }

    this.barcodeLabelPrintCandidates.set(items);
    this.barcodeLabelPrintDialogVisible.set(true);
  }

  private openOrDownloadLabelBlob(blob: Blob): void {
    try {
      openPdfBlobInNewTab(blob);
    } catch {
      downloadBlob(blob, 'barcode-labels.pdf');
    }
  }

  private showInfo(key: string): void {
    this.notify('info', key, 2500);
  }

  private showWarn(key: string): void {
    this.notify('warn', key, 3000);
  }

  private showError(key: string): void {
    this.notify('error', key, 3500);
  }

  private showErrorWithDetail(key: string, detail: string): void {
    this.notify('error', key, 3500, detail);
  }

  private showSuccess(key: string, successCount: number, requestedCount: number): void {
    this.notify('success', key, 3000, `${successCount}/${requestedCount}`);
  }

  private showScannerToast(key: string, barcode: string, quantity: number): void {
    this.notify('success', key, 2200, `${barcode} • Qty ${quantity}`);
  }

  private notify(
    severity: 'info' | 'success' | 'warn' | 'error',
    key: string,
    life: number,
    detail?: string,
  ): void {
    this.messageService.add({
      severity,
      summary: this.translocoService.translate(key),
      detail,
      life,
    });
  }

  private extractApiErrors(error: unknown): readonly Record<string, unknown>[] {
    if (!error || typeof error !== 'object') return [];
    const candidate = (error as { error?: unknown }).error;
    if (!candidate || typeof candidate !== 'object') return [];
    const rawErrors = (candidate as { errors?: unknown }).errors;
    return Array.isArray(rawErrors)
      ? rawErrors.filter(
          (entry): entry is Record<string, unknown> => !!entry && typeof entry === 'object',
        )
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
