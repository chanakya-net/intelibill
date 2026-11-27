import { ExpiryPolicyService } from '../../../expiry/services/expiry-policy.service';
import { Component, DestroyRef, LOCALE_ID, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { DecimalPipe, formatNumber } from '@angular/common';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { SelectModule } from 'primeng/select';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs';
import {
  AdjustInventoryBatchRequest,
  BatchFilters,
  BarcodeLabelPrintRequest,
  CountInventoryBatchRequest,
  InventoryAdjustmentDirection,
  InventoryAdjustmentReason,
  InventoryBatchDto,
  UpdateBatchPricesRequest,
} from '../../services/inventory.models';
import { InventoryService } from '../../services/inventory.service';
import { problemCode } from '@intelibill/api';
import { ShopContextService } from '../../../../core/context/shop-context.service';
import type { ShopScope } from '../../../../core/context/shop-scope';
import {
  OPERATING_ACCESS_ERROR_KEYS, OperatingAccessHintService, operatingAccessErrorKey,
} from '../../../billing/status/operating-access-hint.service';
import {
  ReceiptCostCorrectionDraftService, receiptCostPaise, type ReceiptCostDraftRecord,
} from '../../services/receipt-cost-correction-draft.service';
import {
  RECEIPT_VERSION_CONFLICT,
  ReceiptCostCorrectionService,
  type ReceiptCostOutcome,
} from '../../services/receipt-cost-correction.service';
import { BatchesFilterBarComponent } from '../../components/batches-list/batches-filter-bar.component';
import {
  BatchesTableComponent,
  BatchTableAction,
} from '../../components/batches-list/batches-table.component';
import {
  BarcodeLabelPrintDialogComponent,
  type BarcodeLabelPrintCandidate,
} from '../../components/barcode-label-print-dialog.component';
import { ShopPermissionsService } from '../../../../core/layout/shop-permissions.service';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../../shared/primeng-pt.config';
import {
  formatLocalIsoDate,
  formatUtcIsoInstant,
  parseDateOnlyAsLocalDate,
} from '../../../../shared/utils/date-time.util';
import { downloadBlob, openPdfBlobInNewTab } from '../../../../shared/utils/blob-download.util';
import { InputValidators } from '../../../../shared/forms/input-validation';
import { hasProblemCode } from '../../../../shared/utils/problem-code.util';
import {
  QuantityMessage,
  entryScaleOf,
  quantityPrecisionMessage,
} from '../../../../shared/quantity/quantity-messages';
const BATCH_VERSION_CONFLICT = 'Inventory.BatchVersionConflict';
const BATCH_CONSUMED = 'Inventory.BatchConsumed';
const QUANTITY_PRECISION_EXCEEDED = 'Inventory.QuantityPrecisionExceeded';
const RECEIPT_COST_ERRORS: Readonly<Record<string, string>> = {
  'PurchaseReceipt.EvidenceMissing': 'inventory.receiptCostEvidenceMissing',
  'PurchaseReceipt.CostUnchanged': 'inventory.receiptCostUnchanged',
  'PurchaseReceipt.SourceIneligible': 'inventory.receiptCostSourceIneligible',
  'PurchaseReceipt.Busy': 'inventory.receiptCostBusy',
  'Payment.CorrectionBelowAllocated': 'inventory.receiptCostBelowAllocated',
};
const RECEIPT_BATCH_VOIDED = 'PurchaseReceipt.BatchVoided';
/** Matches the server's money column (numeric(10,2)). */
const MAX_RECEIPT_TOTAL_COST = 99_999_999.99;
interface SelectOption<T extends string | boolean> {
  label: string;
  value: T;
}
@Component({
  selector: 'app-inventory-batches-list-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    DecimalPipe,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    TextareaModule,
    ToastModule,
    SelectModule,
    BatchesFilterBarComponent,
    BatchesTableComponent,
    BarcodeLabelPrintDialogComponent,
  ],
  providers: [MessageService],
  templateUrl: './inventory-batches-list-page.component.html',
  styleUrl: './inventory-batches-list-page.component.scss',
})
export class InventoryBatchesListPageComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly translocoService = inject(TranslocoService);
  private readonly activeLanguage = toSignal(this.translocoService.langChanges$, {
    initialValue: this.translocoService.getActiveLang(),
  });
  private readonly messageService = inject(MessageService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly locale = inject(LOCALE_ID);
  /** Cost corrections move the supplier payable; the server accepts them from an owner only. */
  readonly canCorrectReceiptCost = inject(ShopPermissionsService).isOwnerOfActiveShop;
  private readonly shopContext = inject(ShopContextService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly receiptCostService = inject(ReceiptCostCorrectionService);
  private readonly receiptCostDrafts = inject(ReceiptCostCorrectionDraftService);
  private readonly operatingAccess = inject(OperatingAccessHintService);
  readonly costAccessNotice = this.operatingAccess.blockingMessage;
  readonly costSecurityHidden = computed(() =>
    this.operatingAccess.blockingMessage() === OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended'],
  );
  readonly pendingCostCorrections = signal<readonly ReceiptCostDraftRecord[]>([]);
  private costDraftRead = 0;
  private batchRead = 0;
  private readonly authorizedBatchScope = signal<ShopScope | null>(null);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;
  private readonly expiryPolicies = inject(ExpiryPolicyService);
  readonly batches = signal<InventoryBatchDto[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal(false);
  readonly selectedBatchIds = signal<readonly string[]>([]);
  readonly isEditDialogOpen = signal(false);
  readonly isAdjustmentDialogOpen = signal(false);
  readonly isCountDialogOpen = signal(false);
  readonly isCountSaving = signal(false);
  readonly barcodeLabelPrintDialogVisible = signal(false);
  readonly barcodeLabelPrintCandidates = signal<readonly BarcodeLabelPrintCandidate[]>([]);
  readonly isSaving = signal(false);
  readonly isAdjustmentSaving = signal(false);
  readonly isCostCorrectionDialogOpen = signal(false);
  readonly isCostCorrectionSaving = signal(false);
  readonly selectedBatch = signal<InventoryBatchDto | null>(null);
  readonly batchFilters = signal<BatchFilters>({
    search: '',
    status: 'all',
    fromDate: '',
    toDate: '',
  });
  readonly filteredBatches = computed(() => {
    const filters = this.batchFilters();
    const query = filters.search.trim().toLowerCase();
    const fromBoundaryMs = this.toDateBoundary(filters.fromDate, false);
    const toBoundaryMs = this.toDateBoundary(filters.toDate, true);
    return this.batches().filter((batch) => {
      if (!this.matchesStatusFilter(batch, filters.status)) return false;
      if (query && !this.matchesSearchFilter(batch, query)) return false;
      if (!this.matchesDateRange(batch, fromBoundaryMs, toBoundaryMs)) return false;
      return true;
    });
  });
  readonly printableCandidates = computed(() => {
    const selectedIds = new Set(this.selectedBatchIds());
    return this.filteredBatches()
      .filter((batch) => selectedIds.has(batch.id) && !batch.isVoided)
      .map((batch) => this.mapBatchToPrintCandidate(batch));
  });
  readonly taxModeOptions = signal([
    { labelKey: 'inventory.withTax', value: true },
    { labelKey: 'inventory.withoutTax', value: false },
  ]);
  readonly directionOptions = computed<SelectOption<InventoryAdjustmentDirection>[]>(() => {
    this.activeLanguage();
    return [
      { label: this.translate('inventory.adjustmentDirection.decrease'), value: 'Decrease' },
      { label: this.translate('inventory.adjustmentDirection.increase'), value: 'Increase' },
    ];
  });
  private readonly decreaseReasonOptions = computed<SelectOption<InventoryAdjustmentReason>[]>(
    () => {
      this.activeLanguage();
      return [
        { label: this.translate('inventory.adjustmentReason.damaged'), value: 'Damaged' },
        { label: this.translate('inventory.adjustmentReason.expired'), value: 'Expired' },
        { label: this.translate('inventory.adjustmentReason.stolen'), value: 'Stolen' },
        { label: this.translate('inventory.adjustmentReason.missingLost'), value: 'MissingLost' },
        {
          label: this.translate('inventory.adjustmentReason.stockCountCorrection'),
          value: 'StockCountCorrection',
        },
        { label: this.translate('inventory.adjustmentReason.otherLoss'), value: 'OtherLoss' },
      ];
    },
  );
  private readonly increaseReasonOptions = computed<SelectOption<InventoryAdjustmentReason>[]>(
    () => {
      this.activeLanguage();
      return [
        { label: this.translate('inventory.adjustmentReason.foundStock'), value: 'FoundStock' },
        {
          label: this.translate('inventory.adjustmentReason.stockCountCorrection'),
          value: 'StockCountCorrection',
        },
        {
          label: this.translate('inventory.adjustmentReason.returnRestockCorrection'),
          value: 'ReturnRestockCorrection',
        },
        { label: this.translate('inventory.adjustmentReason.otherGain'), value: 'OtherGain' },
      ];
    },
  );
  private readonly adjustmentFormValue = signal({
    direction: 'Decrease' as InventoryAdjustmentDirection,
    reason: 'Damaged' as InventoryAdjustmentReason,
    quantity: 1,
    performedAt: null as Date | null,
    notes: '',
  });
  readonly adjustmentReasonOptions = computed(() =>
    this.adjustmentFormValue().direction === 'Increase'
      ? this.increaseReasonOptions()
      : this.decreaseReasonOptions(),
  );
  /** The refusal under the adjustment quantity: its unit and allowed decimals; null when it fits the item's unit. */
  adjustmentQuantityError(): QuantityMessage | null {
    return this.precisionError(this.adjustmentForm.controls.quantity.value);
  }
  /** The refusal under the counted quantity: its unit and allowed decimals; null when it fits the item's unit. */
  countQuantityError(): QuantityMessage | null {
    return this.precisionError(this.countForm.controls.countedQuantity.value);
  }
  private precisionError(value: number): QuantityMessage | null {
    const batch = this.selectedBatch();
    return batch
      ? quantityPrecisionMessage(
          value,
          batch.uom || batch.itemName,
          batch.quantityScale,
          this.translocoService.getActiveLang(),
        )
      : null;
  }
  readonly adjustmentPreview = computed(() => {
    const batch = this.selectedBatch();
    if (!batch) return null;
    const value = this.adjustmentFormValue();
    const quantity = Number(value.quantity) || 0;
    const signed = value.direction === 'Increase' ? quantity : -quantity;
    return {
      batchQuantityBefore: batch.quantity,
      batchQuantityAfter: Number((batch.quantity + signed).toFixed(3)),
      unitCost: batch.costPrice,
      costImpact: Number((signed * batch.costPrice).toFixed(2)),
    };
  });
  readonly countForm = this.formBuilder.nonNullable.group({
    countedQuantity: [
      0,
      [...InputValidators.nonNegativeNumber(), InputValidators.maxFractionDigits(3)],
    ],
    reason: ['', InputValidators.requiredText(500)],
  });
  private readonly countedQuantityValue = signal(0);
  readonly countPreview = computed(() => {
    const batch = this.selectedBatch();
    if (!batch) return null;
    const counted = Number(this.countedQuantityValue()) || 0;
    return {
      systemQuantity: batch.quantity,
      countedQuantity: counted,
      difference: Number((counted - batch.quantity).toFixed(3)),
    };
  });
  // One key per distinct submission: an unchanged resend after a lost response replays the saved
  // count instead of posting it twice, while an edited count is a new submission.
  private countSubmission: { readonly signature: string; readonly operationKey: string } | null =
    null;
  // Retail prices and lot details only. Quantity is changed by a stock count and purchase cost by
  // a receipt cost correction, so a price edit never replaces the lot or reposts its payable.
  readonly editForm = this.formBuilder.nonNullable.group(
    {
      batchNumber: ['', InputValidators.requiredText(80)],
      mrp: [0, InputValidators.nonNegativeNumber()],
      salesPrice: [0, InputValidators.nonNegativeNumber()],
      taxRatePercent: [0, InputValidators.percentage()],
      taxIncluded: [false],
      expiryDate: [null as Date | null],
      manufacturingDate: [null as Date | null],
      reason: ['', InputValidators.optionalText(500)],
    },
    {
      validators: InputValidators.fieldLessThanOrEqual('salesPrice', 'mrp', 'salesPriceExceedsMrp'),
    },
  );
  readonly costCorrectionForm = this.formBuilder.nonNullable.group({
    revisedTotalCost: [
      0,
      [
        ...InputValidators.nonNegativeNumber(),
        Validators.max(MAX_RECEIPT_TOTAL_COST),
        InputValidators.maxFractionDigits(2),
      ],
    ],
    reason: ['', InputValidators.requiredText(500)],
  });
  // The input clears to null; an empty field has no delta to show (it is not a total of zero).
  private readonly revisedTotalCostValue = signal<number | null>(0);
  /** A batch without a receipt value on record cannot be corrected: there is nothing to diff. */
  readonly costCorrectionBlocked = computed(() => {
    const batch = this.selectedBatch();
    return (
      !batch ||
      batch.receiptValueId === null ||
      receiptCostPaise(batch.receiptOriginalTotalCost) === null ||
      receiptCostPaise(batch.receiptTotalCost) === null ||
      !batch.receiptVersion
    );
  });
  readonly costCorrectionPreview = computed(() => {
    const batch = this.selectedBatch();
    if (!batch || this.costCorrectionBlocked()) return null;
    const currentTotal = batch.receiptTotalCost ?? 0;
    const revised = this.revisedTotalCostValue();
    const revisedPaise = receiptCostPaise(revised);
    const priorPaise = receiptCostPaise(currentTotal)!;
    const delta = revisedPaise === null ? null : (revisedPaise - priorPaise) / 100;
    return {
      originalTotal: batch.receiptOriginalTotalCost,
      currentTotal,
      delta,
      deltaText: delta === null ? null : this.formatSignedAmount(delta),
    };
  });
  readonly selectedCostDraft = computed(() => {
    const receipt = this.selectedBatch()?.receiptValueId;
    return this.pendingCostCorrections().find((entry) => entry.draft.receiptValueId === receipt);
  });
  readonly adjustmentForm = this.formBuilder.nonNullable.group({
    direction: ['Decrease' as InventoryAdjustmentDirection, [Validators.required]],
    reason: ['Damaged' as InventoryAdjustmentReason, [Validators.required]],
    quantity: [1, [...InputValidators.positiveNumber(), InputValidators.maxFractionDigits(3)]],
    performedAt: [null as Date | null],
    notes: [''],
  });
  constructor() {
    this.adjustmentForm.valueChanges.subscribe(() =>
      this.adjustmentFormValue.set(this.adjustmentForm.getRawValue()),
    );
    this.adjustmentForm.controls.direction.valueChanges.subscribe((direction) => {
      const options =
        direction === 'Increase' ? this.increaseReasonOptions() : this.decreaseReasonOptions();
      const currentReason = this.adjustmentForm.controls.reason.value;
      if (!options.some((option) => option.value === currentReason)) {
        this.adjustmentForm.controls.reason.setValue(options[0].value);
      }
      this.updateAdjustmentQuantityValidators();
    });
    this.adjustmentForm.controls.reason.valueChanges.subscribe(() =>
      this.updateAdjustmentNotesValidators(),
    );
    this.countForm.controls.countedQuantity.valueChanges.subscribe((value) =>
      this.countedQuantityValue.set(value),
    );
    this.costCorrectionForm.controls.revisedTotalCost.valueChanges.subscribe((value) =>
      this.revisedTotalCostValue.set(value),
    );
    this.receiptCostDrafts.watch().pipe(takeUntilDestroyed()).subscribe(() => void this.refreshCostDrafts());
    effect(() => {
      void this.refreshCostDrafts();
      if (!this.canCorrectReceiptCost()) this.isCostCorrectionDialogOpen.set(false);
    });
    effect(() => {
      if (!this.costSecurityHidden()) return;
      this.authorizedBatchScope.set(null);
      this.batches.set([]);
      this.selectedBatch.set(null);
      this.selectedBatchIds.set([]);
      this.isCostCorrectionDialogOpen.set(false);
      this.isAdjustmentDialogOpen.set(false);
      this.isCountDialogOpen.set(false);
      this.isEditDialogOpen.set(false);
      this.onBarcodeLabelPrintDialogClosed();
    });
    this.loadBatches();
  }
  onFiltersChange(filters: BatchFilters): void {
    this.batchFilters.set(filters);
  }
  onBatchTableAction(event: BatchTableAction): void {
    if (event.action === 'refresh') return this.loadBatches();
    const batch = this.batches().find((entry) => entry.id === event.batchId);
    if (!batch) return;
    if (event.action === 'printLabels')
      return this.openBarcodeLabelPrintDialog([this.mapBatchToPrintCandidate(batch)]);
    if (event.action === 'edit') return this.onEditBatch(batch);
    if (event.action === 'adjust') return this.onAdjustBatch(batch);
    if (event.action === 'count') return this.onCountBatch(batch);
    if (event.action === 'correctCost') return this.onCorrectReceiptCost(batch);
    if (event.action === 'void') return this.onVoidBatch(batch.id);
    this.onEditBatch(batch);
  }

  onBatchSelectionChange(batchIds: readonly string[]): void {
    this.selectedBatchIds.set([...batchIds]);
  }

  onPrintSelectedBatches(): void {
    this.openBarcodeLabelPrintDialog(this.printableCandidates());
  }

  onBatchTableSelect(batchId: string): void {
    const batch = this.batches().find((entry) => entry.id === batchId);
    if (!batch) return;
    this.onEditBatch(batch);
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
        this.selectedBatchIds.set([]);
      },
      error: () => this.showError('inventory.barcodeLabels.printFailed'),
    });
  }

  onBarcodeLabelPrintDialogClosed(): void {
    this.barcodeLabelPrintDialogVisible.set(false);
    this.barcodeLabelPrintCandidates.set([]);
  }

  loadBatches(): void {
    const scope = this.shopContext.scope();
    const read = ++this.batchRead;
    const isCurrent = () => read === this.batchRead && scope === this.shopContext.scope();
    this.authorizedBatchScope.set(null);
    this.loading.set(true);
    this.loadError.set(false);
    this.inventoryService
      .getInventoryBatches()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        if (isCurrent()) this.loading.set(false);
      }))
      .subscribe({
        next: (data) => {
          if (!isCurrent() || this.costSecurityHidden()) return;
          this.batches.set([...data]);
          this.authorizedBatchScope.set(scope);
        },
        error: (error: HttpErrorResponse) => {
          if (scope !== this.shopContext.scope()) return;
          this.operatingAccess.reportRefusal(error, scope);
          if (!isCurrent()) return;
          this.batches.set([]);
          this.loadError.set(true);
          this.showError('inventory.loadBatchesError');
        },
      });
  }

  costRecoveryBatchNumber({ draft }: ReceiptCostDraftRecord): string | undefined {
    const scope = this.authorizedBatchScope();
    if (!scope || scope !== this.shopContext.scope() || this.costSecurityHidden()) return undefined;
    return this.batches().find((batch) =>
      batch.id === draft.batchId && batch.receiptValueId === draft.receiptValueId)?.batchNumber;
  }
  onEditBatch(batch: InventoryBatchDto): void {
    this.selectedBatch.set(batch);
    this.editForm.reset({
      batchNumber: batch.batchNumber,
      mrp: batch.mrp,
      salesPrice: batch.salesPrice,
      taxRatePercent: batch.taxRatePercent,
      taxIncluded: batch.taxIncluded,
      expiryDate: this.parseDateOnly(batch.expiryDate),
      manufacturingDate: this.parseDateOnly(batch.manufacturingDate),
      reason: '',
    });
    this.isEditDialogOpen.set(true);
  }
  onSaveEdit(): void {
    const batch = this.selectedBatch();
    if (!batch || this.editForm.invalid || this.isSaving()) return;
    const value = this.editForm.getRawValue();
    const expiry = this.dateOnlyOrNull(value.expiryDate), manufacturing = this.dateOnlyOrNull(value.manufacturingDate);
    if (expiry && manufacturing && manufacturing > expiry) { this.showError('expiry.dateOrder'); return; }
    const datesChanged = expiry !== batch.expiryDate || manufacturing !== batch.manufacturingDate;
    if (datesChanged && !value.reason.trim()) { this.showError('expiry.reasonRequired'); return; }
    this.isSaving.set(true);
    const payload: UpdateBatchPricesRequest = {
      expectedVersion: batch.version,
      mrp: value.mrp,
      salesPrice: value.salesPrice,
      taxRatePercent: value.taxRatePercent,
      taxIncluded: value.taxIncluded,
      batchNumber: this.nullable(value.batchNumber),
      expiryDate: this.dateOnlyOrNull(value.expiryDate),
      manufacturingDate: this.dateOnlyOrNull(value.manufacturingDate),
      reason: this.nullable(value.reason),
    };
    if (datesChanged) void this.expiryPolicies.invalidate(batch.shopId).catch(() => this.showError('expiry.offline.refresh'));
    this.inventoryService
      .updateBatchPrices(batch.id, payload)
      .pipe(finalize(() => this.isSaving.set(false)))
      .subscribe({
        next: () => {
          if (datesChanged) void this.expiryPolicies.invalidate(batch.shopId).catch(() => this.showError('expiry.offline.refresh'));
          this.showSuccess('inventory.batchPricesUpdated');
          this.isEditDialogOpen.set(false);
          this.loadBatches();
        },
        error: (err: HttpErrorResponse) => {
          if (this.errorCode(err) === BATCH_VERSION_CONFLICT) {
            this.isEditDialogOpen.set(false);
            this.showError('inventory.editVersionConflict');
            this.loadBatches();
            return;
          }
          const detail = err.error?.detail || this.translate('inventory.updateBatchError');
          this.messageService.add({
            severity: 'error',
            summary: this.translocoService.translate('common.error'),
            detail,
          });
        },
      });
  }
  onCorrectReceiptCost(batch: InventoryBatchDto): void {
    if (batch.isVoided || !this.canCorrectReceiptCost() || this.costSecurityHidden()) return;
    this.selectedBatch.set(batch);
    const currentTotal = batch.receiptTotalCost ?? 0;
    this.costCorrectionForm.reset({ revisedTotalCost: currentTotal, reason: '' });
    this.isCostCorrectionDialogOpen.set(true);
    if (!this.costCorrectionBlocked()) void this.operatingAccess.refresh();
  }

  async onSaveCostCorrection(): Promise<void> {
    const scope = this.shopContext.scope();
    const batch = this.selectedBatch();
    const preview = this.costCorrectionPreview();
    if (!scope?.shopId || !batch?.receiptValueId || batch.shopId !== scope.shopId || !batch.receiptVersion ||
        !preview || preview.originalTotal === null || !preview.delta ||
        this.costCorrectionForm.invalid || this.selectedCostDraft() || this.isCostCorrectionSaving())
      return;
    const current = () => this.ownsCostCorrection(scope) && !this.costSecurityHidden() &&
      this.isCostCorrectionDialogOpen() && this.selectedBatch() === batch && this.batches().some(
        (row) => row.id === batch.id && row.receiptValueId === batch.receiptValueId &&
          row.receiptVersion === batch.receiptVersion && !row.isVoided,
      );
    if (!current()) return;

    const { revisedTotalCost, reason } = this.costCorrectionForm.getRawValue();
    await this.runCostCorrection(scope, () => this.receiptCostService.submit(scope, {
      receiptValueId: batch.receiptValueId!,
      batchId: batch.id,
      batchNumber: batch.batchNumber,
      originalTotal: preview.originalTotal!,
      priorTotal: preview.currentTotal,
      request: { expectedVersion: batch.receiptVersion!, revisedTotalCost, reason: reason.trim() },
    }, current));
  }

  async onRecoverCostCorrection(draft: ReceiptCostDraftRecord): Promise<void> {
    const scope = this.shopContext.scope();
    if (!scope?.shopId || !draft.pending || this.isCostCorrectionSaving() ||
        draft.userId !== scope.userId || draft.shopId !== scope.shopId || !this.ownsCostCorrection(scope))
      return;
    const current = () => this.ownsCostCorrection(scope) && this.pendingCostCorrections().some(
      (entry) => entry.targetId === draft.targetId && entry.pending?.operationKey === draft.pending?.operationKey,
    );
    await this.runCostCorrection(scope, async () => {
      if (this.costSecurityHidden()) await this.operatingAccess.refresh();
      return this.receiptCostService.recover(scope, draft, current, () => !this.costSecurityHidden());
    });
  }

  private ownsCostCorrection(scope: ShopScope): boolean {
    return !this.destroyRef.destroyed && this.shopContext.scope() === scope && this.canCorrectReceiptCost();
  }

  private async refreshCostDrafts(): Promise<void> {
    const scope = this.shopContext.scope();
    const read = ++this.costDraftRead;
    if (!scope?.shopId || !this.canCorrectReceiptCost()) {
      this.pendingCostCorrections.set([]);
      return;
    }
    try {
      const records = await this.receiptCostDrafts.list(scope);
      if (read === this.costDraftRead && this.ownsCostCorrection(scope))
        this.pendingCostCorrections.set(records.filter((record) => record.pending !== null));
    } catch {
      // A send still requires a successful durable transaction.
    }
  }

  private async runCostCorrection(scope: ShopScope, run: () => Promise<ReceiptCostOutcome>): Promise<void> {
    this.isCostCorrectionSaving.set(true);
    try {
      const outcome = await run();
      if (!this.ownsCostCorrection(scope) || outcome.kind === 'skipped') return;
      if (outcome.kind === 'confirmed') {
        this.messageService.add({
          severity: 'success',
          summary: this.translocoService.translate('inventory.receiptCostCorrected', {
            delta: this.formatSignedAmount(outcome.response.delta),
          }),
          life: 4000,
        });
        this.isCostCorrectionDialogOpen.set(false);
        this.loadBatches();
      } else {
        this.showCostCorrectionRefusal(outcome, scope);
      }
    } catch {
      if (this.ownsCostCorrection(scope)) this.showError('inventory.receiptCostRecoveryUnavailable');
    } finally {
      await this.refreshCostDrafts();
      this.isCostCorrectionSaving.set(false);
    }
  }

  private showCostCorrectionRefusal(
    outcome: Extract<ReceiptCostOutcome, { kind: 'pending' | 'rejected' }>, scope: ShopScope,
  ): void {
    const error = outcome.error;
    const code = error instanceof HttpErrorResponse ? problemCode(error.error) : null;
    if (code === 'Licence.SecuritySuspended') {
      this.operatingAccess.reportRefusal(error, scope);
      void this.operatingAccess.refresh();
    }
    if (outcome.kind === 'rejected' && (code === RECEIPT_VERSION_CONFLICT || code === RECEIPT_BATCH_VOIDED)) {
      this.isCostCorrectionDialogOpen.set(false);
      this.loadBatches();
      if (code === RECEIPT_VERSION_CONFLICT) {
        this.showError('inventory.receiptCostVersionConflict');
        return;
      }
    }
    const key = operatingAccessErrorKey(error) ?? (code ? RECEIPT_COST_ERRORS[code] : null);
    if (key) {
      this.showError(key);
      return;
    }
    const detail: unknown = error instanceof HttpErrorResponse ? error.error?.detail : null;
    this.messageService.add({
      severity: 'error',
      summary: this.translate('common.error'),
      detail: typeof detail === 'string' && detail.trim() ? detail :
        this.translate(outcome.kind === 'pending' ? 'inventory.receiptCostPending' : 'inventory.receiptCostError'),
    });
  }

  onAdjustBatch(batch: InventoryBatchDto): void {
    if (batch.isVoided) return;
    this.selectedBatch.set(batch);
    this.adjustmentForm.reset({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 1,
      performedAt: null,
      notes: '',
    });
    this.updateAdjustmentQuantityValidators();
    this.updateAdjustmentNotesValidators();
    this.adjustmentFormValue.set(this.adjustmentForm.getRawValue());
    this.isAdjustmentDialogOpen.set(true);
  }
  onSaveAdjustment(): void {
    const batch = this.selectedBatch();
    if (!batch || this.adjustmentForm.invalid || this.isAdjustmentSaving()) return;
    this.isAdjustmentSaving.set(true);
    const value = this.adjustmentForm.getRawValue();
    const payload: AdjustInventoryBatchRequest = {
      direction: value.direction,
      reason: value.reason,
      quantity: value.quantity,
      performedAt: this.toIsoTimestamp(value.performedAt),
      notes: this.nullable(value.notes),
    };
    this.inventoryService
      .adjustInventoryBatch(batch.id, payload)
      .pipe(finalize(() => this.isAdjustmentSaving.set(false)))
      .subscribe({
        next: () => {
          this.showSuccess('inventory.batchAdjusted');
          this.isAdjustmentDialogOpen.set(false);
          this.loadBatches();
        },
        error: (err) => {
          const detail = hasProblemCode(err, QUANTITY_PRECISION_EXCEEDED)
            ? this.translate('inventory.quantityPrecisionExceeded')
            : err.error?.detail || this.translate('inventory.adjustBatchError');
          this.messageService.add({
            severity: 'error',
            summary: this.translocoService.translate('common.error'),
            detail,
          });
        },
      });
  }
  onCountBatch(batch: InventoryBatchDto): void {
    if (batch.isVoided) return;
    this.selectedBatch.set(batch);
    this.countSubmission = null;
    this.countForm.controls.countedQuantity.setValidators([
      ...InputValidators.nonNegativeNumber(),
      InputValidators.maxFractionDigits(entryScaleOf(batch.quantityScale)),
    ]);
    this.countForm.reset({ countedQuantity: batch.quantity, reason: '' });
    this.countedQuantityValue.set(batch.quantity);
    this.isCountDialogOpen.set(true);
  }
  onSaveCount(): void {
    const batch = this.selectedBatch();
    if (!batch || this.countForm.invalid || this.isCountSaving()) return;
    const value = this.countForm.getRawValue();
    const payload: CountInventoryBatchRequest = {
      expectedVersion: batch.version,
      countedQuantity: value.countedQuantity,
      reason: value.reason.trim(),
      operationKey: this.countOperationKey(batch, value.countedQuantity, value.reason.trim()),
    };
    this.isCountSaving.set(true);
    this.inventoryService
      .countInventoryBatch(batch.id, payload)
      .pipe(finalize(() => this.isCountSaving.set(false)))
      .subscribe({
        next: (result) => {
          this.countSubmission = null;
          if (result.adjustmentNumber) {
            this.messageService.add({
              severity: 'success',
              summary: this.translocoService.translate('inventory.countSaved', {
                number: result.adjustmentNumber,
              }),
              life: 4000,
            });
          } else {
            this.showSuccess('inventory.countConfirmed');
          }
          this.isCountDialogOpen.set(false);
          this.loadBatches();
        },
        error: (err: HttpErrorResponse) => {
          if (this.errorCode(err) === BATCH_VERSION_CONFLICT) {
            // The count was taken against stock that has since moved. It is never resent against the
            // refreshed quantity: the counter has to look at the shelf again.
            this.countSubmission = null;
            this.isCountDialogOpen.set(false);
            this.showError('inventory.countVersionConflict');
            this.loadBatches();
            return;
          }
          this.showError(
            hasProblemCode(err, QUANTITY_PRECISION_EXCEEDED)
              ? 'inventory.quantityPrecisionExceeded'
              : 'inventory.countError',
          );
        },
      });
  }
  onVoidBatch(batchId: string): void {
    this.inventoryService.voidInventoryBatch(batchId).subscribe({
      next: () => {
        this.showSuccess('inventory.batchVoided');
        this.loadBatches();
      },
      error: (err: HttpErrorResponse) =>
        this.showError(
          this.errorCode(err) === BATCH_CONSUMED
            ? 'inventory.batchConsumed'
            : 'inventory.voidBatchError',
        ),
    });
  }
  private countOperationKey(
    batch: InventoryBatchDto,
    countedQuantity: number,
    reason: string,
  ): string {
    const signature = JSON.stringify([batch.id, batch.version, countedQuantity, reason]);
    if (this.countSubmission?.signature !== signature)
      this.countSubmission = { signature, operationKey: `count-${crypto.randomUUID()}` };
    return this.countSubmission.operationKey;
  }
  private formatSignedAmount(value: number): string {
    const sign = value > 0 ? '+' : value < 0 ? '-' : '';
    const amount = formatNumber(Math.abs(value), this.locale, '1.2-2');
    return `${sign}${this.translate('common.currencySymbol')}${amount}`;
  }
  private errorCode(err: HttpErrorResponse): string | null {
    const title: unknown = err.error?.title;
    return typeof title === 'string' ? title : null;
  }
  private nullable(value: string): string | null {
    return value.trim().length > 0 ? value.trim() : null;
  }
  private parseDateOnly(value: string | null): Date | null {
    if (!value) return null;
    const date = parseDateOnlyAsLocalDate(value);
    return Number.isNaN(date.getTime()) ? null : date;
  }
  private dateOnlyOrNull(value: Date | null): string | null {
    return value ? formatLocalIsoDate(value) : null;
  }
  private toIsoTimestamp(value: Date | null): string | null {
    return value ? formatUtcIsoInstant(value) : null;
  }
  private updateAdjustmentQuantityValidators(): void {
    const validators: ValidatorFn[] = [
      ...InputValidators.positiveNumber(),
      InputValidators.maxFractionDigits(entryScaleOf(this.selectedBatch()?.quantityScale)),
    ];
    const batch = this.selectedBatch();
    if (batch && this.adjustmentForm.controls.direction.value === 'Decrease')
      validators.push(Validators.max(batch.quantity));
    this.adjustmentForm.controls.quantity.setValidators(validators);
    this.adjustmentForm.controls.quantity.updateValueAndValidity({ emitEvent: false });
  }
  private updateAdjustmentNotesValidators(): void {
    const reason = this.adjustmentForm.controls.reason.value;
    const validators =
      reason === 'OtherLoss' || reason === 'OtherGain'
        ? InputValidators.requiredText(500)
        : InputValidators.optionalText(500);
    this.adjustmentForm.controls.notes.setValidators(validators);
    this.adjustmentForm.controls.notes.updateValueAndValidity({ emitEvent: false });
  }
  private matchesSearchFilter(batch: InventoryBatchDto, query: string): boolean {
    return (
      batch.itemName.toLowerCase().includes(query) ||
      batch.barcode.toLowerCase().includes(query) ||
      batch.batchNumber.toLowerCase().includes(query)
    );
  }
  private matchesStatusFilter(batch: InventoryBatchDto, status: BatchFilters['status']): boolean {
    return status === 'all' ? true : status === 'active' ? !batch.isVoided : batch.isVoided;
  }
  private matchesDateRange(
    batch: InventoryBatchDto,
    fromBoundaryMs: number | null,
    toBoundaryMs: number | null,
  ): boolean {
    if (!fromBoundaryMs && !toBoundaryMs) return true;
    const batchCreatedAt = Date.parse(batch.createdAt);
    if (Number.isNaN(batchCreatedAt)) return true;
    if (fromBoundaryMs && batchCreatedAt < fromBoundaryMs) return false;
    if (toBoundaryMs && batchCreatedAt > toBoundaryMs) return false;
    return true;
  }
  private toDateBoundary(value: string, includeDayEnd: boolean): number | null {
    const normalized = value.trim();
    if (!normalized) return null;
    const date = new Date(normalized);
    if (Number.isNaN(date.getTime())) return null;
    if (includeDayEnd) date.setHours(23, 59, 59, 999);
    else date.setHours(0, 0, 0, 0);
    return date.getTime();
  }
  private mapBatchToPrintCandidate(batch: InventoryBatchDto): BarcodeLabelPrintCandidate {
    return {
      itemId: batch.itemId,
      itemName: batch.itemName,
      barcode: batch.barcode,
      inventoryBatchId: batch.id,
      quantity: batch.quantity,
    };
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
  private showSuccess(messageKey: string): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate(messageKey),
      life: 3000,
    });
  }
  private showError(messageKey: string): void {
    this.messageService.add({ severity: 'error', summary: this.translate(messageKey), life: 3500 });
  }
  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
