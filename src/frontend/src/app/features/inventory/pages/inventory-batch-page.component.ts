import { HttpErrorResponse } from '@angular/common/http';
import { ExpiryPolicyService } from '../../expiry/services/expiry-policy.service';
import {
  Component,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { assertCurrentScope, isScopeChangeError, ScopeMismatchError } from '../../../core/context/scoped-request';
import { hasProblemCode } from '../../../shared/utils/problem-code.util';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { AudioService } from '../../../core/services/audio.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import {
  InventoryInboundDraftRow,
  InventoryInboundSubmission,
  InventoryInboundSubmissionChunk,
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
import { OPERATING_ACCESS_ERROR_KEYS, OperatingAccessHintService, operatingAccessErrorKey } from '../../billing/status/operating-access-hint.service';
import { assertIntakeResponse, needsIntakeRecovery } from '../services/intake-response';
import { BatchDraftStateService } from '../services/batch-draft-state.service';
import { IntakeRoleRefusalService } from '../services/intake-role-refusal.service';
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
  providers: [MessageService, BatchRowFormStateService, BatchDraftStateService],
  templateUrl: './inventory-batch-page.component.html',
  styleUrl: './inventory-batch-page.component.scss',
  encapsulation: ViewEncapsulation.None,
})
export class InventoryBatchPageComponent {
  @ViewChild(BatchRowFormComponent) batchRowForm!: BatchRowFormComponent;

  private readonly expiryPolicies = inject(ExpiryPolicyService);
  private readonly authService = inject(AuthService);
  private readonly shopContext = inject(ShopContextService);
  private readonly permissions = inject(ShopPermissionsService);
  readonly access = inject(OperatingAccessHintService);
  readonly roleRefusal = inject(IntakeRoleRefusalService);
  private readonly audioService = inject(AudioService);
  private readonly draftState = inject(BatchDraftStateService);
  private readonly inventoryService = inject(InventoryService);
  private readonly messageService = inject(MessageService);
  private readonly rowFormState = inject(BatchRowFormStateService);
  private readonly translocoService = inject(TranslocoService);

  private readonly savingScope = signal<ShopScope | null>(null);
  readonly isSaving = computed(() => this.savingScope() === this.shopContext.scope() && this.savingScope() !== null);
  readonly isScannerOpen = signal(false);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly barcodeLabelPrintDialogVisible = signal(false);
  readonly barcodeLabelPrintCandidates = signal<readonly BarcodeLabelPrintCandidate[]>([]);
  readonly scannerLastAction = signal('');
  readonly scannerSessionCount = signal(0);
  readonly highlightedRowId = signal<string | null>(null);

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  /** A server refusal stays in force until live membership confirms the role again. */
  readonly roleRefused = computed(() => {
    const scope = this.shopContext.scope();
    return !!scope && this.roleRefusal.isRefused(scope);
  });
  readonly draftRoleDenied = computed(() => {
    const scope = this.shopContext.scope();
    return !!scope && (this.roleRefused() || this.permissions.activeShopId() !== scope.shopId ||
      !this.permissions.isOwnerOrManagerOfActiveShop());
  });
  readonly hideDraft = computed(() => this.shopContext.dataRestricted() || this.draftRoleDenied());
  readonly loadingDraft = this.draftState.loadingDraft;
  readonly pendingRows = this.draftState.pendingRows;
  readonly hasInterruptedSubmission = this.draftState.hasInterruptedSubmission;
  readonly canChangeDraft = computed(() => !this.hideDraft() && !this.isSaving() &&
    !this.loadingDraft() && !this.hasInterruptedSubmission());

  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const scope = this.shopContext.scope();
      this.draftState.reset();
      this.rowFormState.resetForm();
      this.saveSummary.set(null);
      this.isScannerOpen.set(false);
      this.barcodeLabelPrintDialogVisible.set(false);
      this.barcodeLabelPrintCandidates.set([]);
      if (scope && this.roleRefused()) untracked(() => void this.roleRefusal.revalidate(scope));
      if (!scope?.shopId || this.hideDraft()) return;

      void this.loadDraftRows(scope);
      void this.access.refresh();
    });
  }

  onRowSubmitted(row: InventoryInboundDraftRow): void {
    if (this.refuseWhileInterrupted()) return;
    void this.persistDraftRow(row);
  }

  onRecheckRole(): void {
    const scope = this.shopContext.scope();
    if (scope) void this.roleRefusal.revalidate(scope);
  }

  onRemoveRow(clientRowId: string): void {
    if (this.refuseWhileInterrupted()) return;
    this.saveSummary.set(null);
    void this.reportIfChangedElsewhere(
      this.draftState.removeRow(this.activeShopId(), clientRowId, this.shopContext.scope() ?? undefined));
  }

  onEditRow(clientRowId: string): void {
    if (this.refuseWhileInterrupted()) return;
    const row = this.pendingRows().find((candidate) => candidate.clientRowId === clientRowId);
    if (!row) return;

    this.saveSummary.set(null);
    this.batchRowForm.populateFromRow(row);
    // The row moves into the form only if this tab's view of the draft was still current.
    void this.reportIfChangedElsewhere(
      this.draftState.removeRow(this.activeShopId(), clientRowId, this.shopContext.scope() ?? undefined),
      () => this.batchRowForm.resetForm());
  }

  onClearAll(): void {
    if (this.refuseWhileInterrupted()) return;
    this.saveSummary.set(null);
    void this.reportIfChangedElsewhere(
      this.draftState.clearRows(this.activeShopId(), this.shopContext.scope() ?? undefined));
  }

  onSaveAll(recovering = this.hasInterruptedSubmission()): void {
    // An interrupted submission can be resent even when no draft rows remain on screen.
    const scope = this.shopContext.scope();
    if (!scope?.shopId || this.hideDraft() || this.isSaving() || this.loadingDraft() ||
        recovering !== this.hasInterruptedSubmission() ||
        (!recovering && (!this.pendingRows().length || this.access.blockingMessage()))) return;
    // An interrupted submission resends its frozen rows unchanged, so it is never held back here.
    if (
      !recovering &&
      this.pendingRows().some((row) => !isPurchaseTaxModeConfirmed(row))
    ) {
      // Drafts kept by earlier releases carry no purchase mode or one copied from the sales mode:
      // the user confirms it per row (edit the row) before anything is sent.
      this.showWarn('inventory.purchaseTaxModeMissingRows');
      return;
    }

    this.savingScope.set(scope);
    void this.saveRowsInChunks(scope, recovering);
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
    if (this.refuseWhileInterrupted()) return;
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
    const mergedRow = await this.draftState.incrementRowQuantity(this.activeShopId(), barcode, this.shopContext.scope() ?? undefined);
    if (mergedRow) {
      this.saveSummary.set(null);
      this.flashRow(mergedRow.clientRowId);
      this.scannerLastAction.set('inventory.scannerActionIncremented');
      this.showScannerToast('inventory.scannerMerged', barcode, mergedRow.quantity);
      void this.audioService.beep();
      return;
    }
    if (this.refuseWhileInterrupted()) return;

    const scanResult = await this.rowFormState.prepareScannedRow(barcode);
    if (this.refuseWhileInterrupted()) return;
    if (scanResult.status === 'added') {
      this.rowFormState.resetForm();
      if (!await this.persistDraftRow(scanResult.row)) return;
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

  private async loadDraftRows(scope: ShopScope): Promise<void> {
    const current = () => this.shopContext.scope() === scope && !this.hideDraft();
    try {
      const rows = await this.draftState.loadDraftRows(scope.shopId, scope, current);
      if (current() && rows.length > 0) this.showInfo('inventory.draftLoaded');
    } catch {
      if (current()) this.showError('inventory.saveFailed');
    }
  }

  private async persistDraftRow(row: InventoryInboundDraftRow): Promise<boolean> {
    const shopId = this.activeShopId();
    if (!shopId) return false;

    this.saveSummary.set(null);
    // A row another tab's frozen save cannot take goes back to the form instead of vanishing.
    return this.reportIfChangedElsewhere(
      this.draftState.addRow(shopId, row, this.shopContext.scope() ?? undefined),
      () => this.batchRowForm.populateFromRow(row));
  }

  /** Another tab changed the stored draft first: the view already shows it; say why nothing moved. */
  private async reportIfChangedElsewhere(change: Promise<boolean>, restore?: () => void): Promise<boolean> {
    const scope = this.shopContext.scope();
    const saved = await change;
    if (saved || scope !== this.shopContext.scope() || this.hideDraft()) return saved;

    restore?.();
    if (this.hasInterruptedSubmission()) this.showWarn('inventory.saveInterruptedLocked');
    else this.showInfo('inventory.draftLoaded');
    return false;
  }

  private async saveRowsInChunks(scope: ShopScope, recovering: boolean): Promise<void> {
    const assertScope = () => {
      assertCurrentScope(this.shopContext, scope, scope);
      if (this.hideDraft()) throw new ScopeMismatchError();
    };
    const succeeded: AddInventoryBatchSucceededRow[] = [];
    const failed: AddInventoryBatchFailedRow[] = [];
    const mutation = this.shopContext.beginMutation();
    let submission: InventoryInboundSubmission | null = null;
    let requested = 0;

    try {
      submission = await this.draftState.prepareSubmission(scope, assertScope);
      if (!submission) {
        this.saveSummary.set(null);
        return;
      }
      requested = submission.chunks.reduce((count, chunk) => count + chunk.rows.length, 0);
      // Retire the prior snapshot even if this tab closes before receiving a response.
      void this.expiryPolicies.invalidate(scope.shopId).catch(() => undefined);
      for (const chunk of submission.chunks) {
        assertScope();
        const response = await this.sendChunk(chunk, scope);
        assertScope();
        assertIntakeResponse(chunk, response);
        succeeded.push(...response.succeeded);
        failed.push(...response.failed);
      }

      const complete = !failed.some(needsIntakeRecovery);
      if (!await this.settle(submission, succeeded, complete, scope, assertScope)) {
        this.saveSummary.set(null);
        return;
      }
      const refusal = failed.flatMap(row => row.errors).find(error => Object.hasOwn(OPERATING_ACCESS_ERROR_KEYS, error.code));
      if (refusal) this.access.reportRowRefusal(refusal.code, scope);
      this.saveSummary.set(this.buildSaveSummary(requested, succeeded, failed));
      if (failed.length === 0) {
        this.showSuccess('inventory.savedSuccess', succeeded.length, requested);
      } else {
        this.showWarn(refusal ? OPERATING_ACCESS_ERROR_KEYS[refusal.code] :
          complete ? 'inventory.savedPartial' : 'inventory.saveInterrupted');
      }
    } catch (error) {
      if (isScopeChangeError(error) || scope !== this.shopContext.scope() || this.hideDraft()) return;
      await this.handleSaveError(error, scope, recovering, submission, requested, succeeded, failed, assertScope);
    } finally {
      mutation.release();
      if (this.savingScope() === scope) this.savingScope.set(null);
    }
  }

  private async sendChunk(chunk: InventoryInboundSubmissionChunk, scope: ShopScope): Promise<AddInventoryBatchResponse> {
    try {
      return await firstValueFrom(this.inventoryService.addInventoryBatch(
        { items: this.mapRowsToRequest(chunk.rows), operationKey: chunk.operationKey }, scope,
      ));
    } finally {
      // Even a lost response or a late answer can follow a committed row in this shop.
      void this.expiryPolicies.invalidate(scope.shopId).catch(() => undefined);
    }
  }

  private async handleSaveError(
    error: unknown, scope: ShopScope, recovering: boolean, submission: InventoryInboundSubmission | null,
    requested: number, succeeded: AddInventoryBatchSucceededRow[], failed: AddInventoryBatchFailedRow[],
    assertScope: () => void,
  ): Promise<void> {
    const key = operatingAccessErrorKey(error);
    this.access.reportRefusal(error, scope);
    const authorityDenied = error instanceof HttpErrorResponse &&
      (error.status === 401 || (error.status === 403 && !key));
    if (authorityDenied) this.roleRefusal.refuse(scope);
    const securityDenied = authorityDenied || hasProblemCode(error, 'Licence.SecuritySuspended');
    if (securityDenied) {
      this.saveSummary.set(null);
      this.barcodeLabelPrintCandidates.set([]);
      this.barcodeLabelPrintDialogVisible.set(false);
    }

    // Only a first-request validation response can return frozen rows to editing.
    const editable = !recovering && succeeded.length === 0 && failed.length === 0 &&
      error instanceof HttpErrorResponse && error.status === 400;
    if (submission) {
      this.draftState.markSubmissionInterrupted(submission);
      if (!securityDenied && (editable || succeeded.length)) {
        const settled = await this.settle(submission, succeeded, editable, scope, assertScope).catch(() => null);
        if (settled === false) return;
      }
      if (scope !== this.shopContext.scope() || this.hideDraft()) return;
      if (!securityDenied && (succeeded.length || failed.length)) {
        this.saveSummary.set(this.buildSaveSummary(requested, succeeded, failed));
      }
      if (!editable) {
        this.showWarn(key ?? 'inventory.saveInterrupted');
        return;
      }
    }
    this.showSaveError(error);
  }

  private showSaveError(error: unknown): void {
    const apiErrors = this.extractApiErrors(error);
    if (apiErrors.some((apiError) => this.getErrorCode(apiError) === 'Inventory.BatchLimitExceeded')) {
      this.showWarn('inventory.batchLimitReached');
    } else if (hasProblemCode(error, 'Inventory.QuantityPrecisionExceeded')) {
      this.showError('inventory.quantityPrecisionExceeded');
    } else {
      const detail = apiErrors.map((apiError) => this.getErrorDescription(apiError)).find(Boolean);
      detail ? this.showErrorWithDetail('inventory.saveFailed', detail) : this.showError('inventory.saveFailed');
    }
  }

  private settle(
    submission: InventoryInboundSubmission, succeeded: readonly AddInventoryBatchSucceededRow[],
    complete: boolean, scope: ShopScope, assertScope: () => void,
  ): Promise<boolean> {
    return this.draftState.settleSubmission(submission, new Set(succeeded.map(row => row.clientRowId)), complete, scope, assertScope);
  }

  private refuseWhileInterrupted(): boolean {
    if (this.canChangeDraft()) return false;
    if (this.hasInterruptedSubmission() && !this.hideDraft()) this.showWarn('inventory.saveInterruptedLocked');
    return true;
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
      inventoryBatchId: row.result.inventoryBatchId,
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
