import { CommonModule } from '@angular/common';
import {
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  RETURN_PAYOUT_DESTINATION_OPTIONS,
  ReturnPayoutDestination,
  SALE_RETURN_CONDITIONS,
  mapPayoutDestinationSelectionToReturnPayoutDestination,
} from '../../services/sale.models';
import type {
  PreviewSaleReturnRequest,
  RecordSaleReturnRequest,
  SaleReturnCreditNoteSummaryDto,
  SaleDto,
  SaleItemDto,
  SaleReturnCondition,
} from '../../services/sale.models';
import { SalesFacade } from '../../state/sales.facade';

interface ReturnLineDraft {
  readonly saleItemId: string;
  readonly selected: boolean;
  readonly quantity: number;
  readonly condition: SaleReturnCondition | null;
  readonly approvedRefundAmount: number | null;
  readonly notes: string;
}

@Component({
  selector: 'app-sale-return-preview-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './sale-return-preview-dialog.component.html',
  styleUrl: './sale-return-preview-dialog.component.scss',
})
export class SaleReturnPreviewDialogComponent {
  private readonly salesFacade = inject(SalesFacade);
  private readonly authService = inject(AuthService);

  readonly isVisible = signal(false);

  @Input()
  set visible(value: boolean) {
    this.isVisible.set(value);
  }
  get visible(): boolean {
    return this.isVisible();
  }
  @Output() readonly visibleChange = new EventEmitter<boolean>();

  readonly saleInput = signal<SaleDto | null>(null);

  @Input()
  set sale(value: SaleDto | null) {
    this.saleInput.set(value);
  }
  get sale(): SaleDto | null {
    return this.saleInput();
  }
  @Input() currency = 'INR';

  readonly returnPreview = this.salesFacade.returnPreview;
  readonly isPreviewLoading = this.salesFacade.loadingReturnPreview;
  readonly isSubmitting = this.salesFacade.submitting;
  readonly previewError = this.salesFacade.returnPreviewErrorMessage;

  readonly returnDrafts = signal<readonly ReturnLineDraft[]>([]);
  readonly validationMessages = signal<readonly string[]>([]);
  readonly dueReductionOverrideAmount = signal<number | null>(null);
  readonly dueOverrideReason = signal('');
  readonly dueOverrideConfirmed = signal(false);
  readonly payoutDestination = signal<number | null>(null);
  readonly creditNoteExpiryMode = signal<'NoExpiry' | '30Days' | '60Days' | '90Days' | 'Custom'>('NoExpiry');
  readonly creditNoteExpiryDate = signal<string | null>(null);
  readonly recordedCreditNoteSummary = signal<SaleReturnCreditNoteSummaryDto | null>(null);

  readonly returnConditionOptions = SALE_RETURN_CONDITIONS;
  readonly payoutDestinationOptions = RETURN_PAYOUT_DESTINATION_OPTIONS;

  readonly creditNoteExpiryOptions = [
    { labelKey: 'sales.returns.preview.creditNoteExpiryNoExpiry', value: 'NoExpiry' },
    { labelKey: 'sales.returns.preview.creditNoteExpiry30Days', value: '30Days' },
    { labelKey: 'sales.returns.preview.creditNoteExpiry60Days', value: '60Days' },
    { labelKey: 'sales.returns.preview.creditNoteExpiry90Days', value: '90Days' },
    { labelKey: 'sales.returns.preview.creditNoteExpiryCustom', value: 'Custom' },
  ];

  readonly activeShopRole = computed(() => {
    const session = this.authService.session();
    if (!session) return '';

    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);

    return activeShop?.role ?? '';
  });

  readonly returnableItems = computed(
    () => this.saleInput()?.items.filter((item) => item.returnableQuantity > 0) ?? [],
  );

  readonly canPreviewReturns = computed(() => {
    const role = this.activeShopRole().toLowerCase();
    return ['owner', 'manager', 'staff'].includes(role) && this.returnableItems().length > 0;
  });

  readonly hasFinancialAccess = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()),
  );
  readonly canSubmitReturns = this.hasFinancialAccess;

  readonly selectedDrafts = computed(() => this.returnDrafts().filter((draft) => draft.selected));

  readonly selectedLineCount = computed(() => this.selectedDrafts().length);

  readonly selectedRefundTotal = computed(() =>
    this.selectedDrafts().reduce((sum, draft) => sum + (draft.approvedRefundAmount ?? 0), 0),
  );

  readonly selectedTaxReversalTotal = computed(() => {
    const sale = this.saleInput();
    if (!sale) return 0;

    return this.selectedDrafts().reduce((sum, draft) => {
      const item = sale.items.find((line) => line.saleItemId === draft.saleItemId);
      return item ? sum + this.getTaxReversalAmount(item, draft.quantity) : sum;
    }, 0);
  });

  readonly selectedItemRefundTotal = computed(() =>
    Math.max(0, this.selectedRefundTotal() - this.selectedTaxReversalTotal()),
  );

  readonly displayedRefundTotal = computed(
    () => this.returnPreview()?.financial?.totalRefundAmount ?? this.selectedRefundTotal(),
  );

  readonly displayedTaxReversalTotal = computed(
    () => this.returnPreview()?.financial?.totalTaxAmount ?? this.selectedTaxReversalTotal(),
  );

  readonly displayedItemRefundTotal = computed(() =>
    Math.max(0, this.displayedRefundTotal() - this.displayedTaxReversalTotal()),
  );

  constructor() {
    effect(() => {
      if (!this.isVisible() || !this.saleInput()) return;
      if (!this.canPreviewReturns()) return;

      if (this.returnDrafts().length > 0) return;
      this.initializeDrafts();
    });

    effect(() => {
      if (!this.isVisible()) return;
      if (!this.salesFacade.lastMutationSucceeded()) return;

      if (this.salesFacade.lastMutationType() === 'record-return') {
        const creditNoteSummary = this.getLatestCreditNoteSummary();
        if (creditNoteSummary) {
          this.recordedCreditNoteSummary.set(creditNoteSummary);
        } else {
          this.close();
        }
        this.salesFacade.clearMutationStatus();
        return;
      }
    });
  }

  close(): void {
    this.visibleChange.emit(false);
    this.validationMessages.set([]);
    this.dueOverrideConfirmed.set(false);
    this.payoutDestination.set(null);
    this.creditNoteExpiryMode.set('NoExpiry');
    this.creditNoteExpiryDate.set(null);
    this.recordedCreditNoteSummary.set(null);
    this.salesFacade.clearSaleReturnPreview();
    this.returnDrafts.set([]);
  }

  toggleReturnLine(item: SaleItemDto, selected: boolean): void {
    this.updateDraft(item.saleItemId, (draft) => ({
      ...draft,
      selected,
      quantity: selected ? item.returnableQuantity : 0,
      approvedRefundAmount:
        selected && this.hasFinancialAccess()
          ? this.getMaxRefundAmount(item, item.returnableQuantity)
          : null,
      condition: selected ? (item.lineType === 'Service' ? null : draft.condition) : null,
      notes: selected ? draft.notes : '',
    }));

    this.salesFacade.clearSaleReturnPreview();
  }

  updateReturnQuantity(item: SaleItemDto, quantity: number | string | null): void {
    const normalizedQuantity = this.clampNumber(Number(quantity), 0, item.returnableQuantity);
    const maxRefund = this.getMaxRefundAmount(item, normalizedQuantity);

    this.updateDraft(item.saleItemId, (draft) => ({
      ...draft,
      quantity: normalizedQuantity,
      approvedRefundAmount:
        draft.selected && this.hasFinancialAccess()
          ? this.clampNumber(draft.approvedRefundAmount ?? maxRefund, 0, maxRefund)
          : null,
    }));

    this.salesFacade.clearSaleReturnPreview();
  }

  updateReturnCondition(item: SaleItemDto, condition: SaleReturnCondition | null): void {
    this.updateDraft(item.saleItemId, (draft) => ({
      ...draft,
      condition,
    }));

    this.salesFacade.clearSaleReturnPreview();
  }

  updateRefundAmount(item: SaleItemDto, refundAmount: number | string | null): void {
    const draft = this.getDraft(item.saleItemId);
    const maxRefund = this.getMaxRefundAmount(item, draft?.quantity ?? item.returnableQuantity);

    this.updateDraft(item.saleItemId, (existing) => ({
      ...existing,
      approvedRefundAmount: this.clampNumber(Number(refundAmount), 0, maxRefund),
    }));

    this.salesFacade.clearSaleReturnPreview();
  }

  updateNotes(item: SaleItemDto, notes: string): void {
    this.updateDraft(item.saleItemId, (draft) => ({
      ...draft,
      notes,
    }));

    this.salesFacade.clearSaleReturnPreview();
  }

  updateDueReductionOverride(value: number | string | null): void {
    const normalized =
      value === '' || value === null
        ? null
        : this.clampNumber(Number(value), 0, Number.MAX_SAFE_INTEGER);

    this.dueReductionOverrideAmount.set(normalized);
    this.dueOverrideConfirmed.set(false);
    this.salesFacade.clearSaleReturnPreview();
  }

  updateDueOverrideReason(value: string): void {
    this.dueOverrideReason.set(value);
    this.salesFacade.clearSaleReturnPreview();
  }

  previewReturn(): void {
    const sale = this.saleInput();
    if (!sale) return;

    const errors = this.validateReturnDrafts();
    this.validationMessages.set(errors);

    if (errors.length > 0) {
      this.salesFacade.clearSaleReturnPreview();
      return;
    }

    const payload: PreviewSaleReturnRequest = {
      dueReductionOverrideAmount: this.hasFinancialAccess()
        ? this.dueReductionOverrideAmount()
        : null,
      dueOverrideReason: this.hasFinancialAccess()
        ? this.normalizeOptional(this.dueOverrideReason())
        : null,
      items: this.selectedDrafts().map((draft) => ({
        saleItemId: draft.saleItemId,
        quantity: draft.quantity,
        lineType: this.getLineType(draft.saleItemId),
        condition: this.getRequestedCondition(draft.saleItemId, draft.condition),
        approvedRefundAmount: this.hasFinancialAccess() ? draft.approvedRefundAmount : null,
        notes: this.normalizeOptional(draft.notes),
      })),
    };

    this.salesFacade.previewSaleReturn(sale.saleId, payload);
  }

  updatePayoutDestination(destination: number | null): void {
    this.payoutDestination.set(destination);
  }

  updateCreditNoteExpiryMode(mode: 'NoExpiry' | '30Days' | '60Days' | '90Days' | 'Custom'): void {
    this.creditNoteExpiryMode.set(mode);
    if (mode !== 'Custom') {
      this.creditNoteExpiryDate.set(null);
    }
  }

  updateCreditNoteExpiryDate(date: string | null): void {
    this.creditNoteExpiryDate.set(date);
  }

  updateDueOverrideConfirmed(confirmed: boolean): void {
    this.dueOverrideConfirmed.set(confirmed);
  }

  printRecordedCreditNote(): void {
    const creditNote = this.recordedCreditNoteSummary();
    if (!creditNote) return;

    window.open(this.getCreditNotePrintUrl(creditNote.code), '_blank');
  }

  submitReturn(): void {
    const sale = this.saleInput();
    if (!sale) return;

    const errors = this.validateSubmit();
    this.validationMessages.set(errors);
    if (errors.length > 0) return;

    const isCreditNote =
      this.mapSelectedPayoutDestination(this.payoutDestination()) ===
      ReturnPayoutDestination.CreditNote;

    const payload = {
      payoutDestination:
        (this.returnPreview()?.financial?.payoutAmount ?? 0) > 0
          ? this.mapSelectedPayoutDestination(this.payoutDestination())
          : null,
      dueReductionOverrideAmount: this.dueReductionOverrideAmount(),
      dueOverrideReason: this.normalizeOptional(this.dueOverrideReason()),
      notes: null,
      items: this.selectedDrafts().map((draft) => ({
        saleItemId: draft.saleItemId,
        quantity: draft.quantity,
        lineType: this.getLineType(draft.saleItemId),
        condition: this.getRequestedCondition(draft.saleItemId, draft.condition),
        approvedRefundAmount: draft.approvedRefundAmount,
        notes: this.normalizeOptional(draft.notes),
      })),
      ...(isCreditNote && {
        creditNoteExpiresAt: this.computeCreditNoteExpiresAt(),
      }),
    } as unknown as RecordSaleReturnRequest;

    this.salesFacade.recordSaleReturn(sale.saleId, payload);
  }

  getDraft(saleItemId: string): ReturnLineDraft | null {
    return this.returnDrafts().find((draft) => draft.saleItemId === saleItemId) ?? null;
  }

  isFullyReturned(item: SaleItemDto): boolean {
    return item.returnableQuantity <= 0;
  }

  getMaxRefundAmount(item: SaleItemDto, quantity: number): number {
    const grossOriginalValue = quantity * item.salesPrice;
    if (item.taxRatePercent <= 0 || item.isPriceIncludingTax) {
      return this.roundMoney(grossOriginalValue);
    }

    return this.roundMoney(grossOriginalValue + (grossOriginalValue * item.taxRatePercent) / 100);
  }

  getPreviewItemName(saleItemId: string): string {
    return (
      this.saleInput()?.items.find((line) => line.saleItemId === saleItemId)?.itemName ||
      'Unknown Item'
    );
  }

  getPreviewLineRefundAmount(saleItemId: string): number | null {
    const line = this.returnPreview()?.lines.find(
      (previewLine) => previewLine.saleItemId === saleItemId,
    );
    return line?.financial?.approvedRefundAmount ?? null;
  }

  isServiceLine(item: SaleItemDto): boolean {
    return item.lineType === 'Service';
  }

  customerName(): string {
    return this.saleInput()?.customerName || 'Walk-in Customer';
  }

  lineTypeLabel(item: SaleItemDto): string {
    return item.lineType === 'Service' ? 'Service' : 'Goods';
  }

  returnLineHint(item: SaleItemDto): string {
    if (this.isServiceLine(item)) {
      return 'Manager approval required';
    }

    return item.returnableQuantity > 0 ? 'Returnable' : 'Fully returned';
  }

  getReturnableLineCount(): number {
    return this.returnableItems().length;
  }

  getLineConditionPlaceholder(item: SaleItemDto): string {
    return this.isServiceLine(item) ? 'Refund-only service' : 'Required';
  }

  decreaseReturnQuantity(item: SaleItemDto): void {
    const draft = this.getDraft(item.saleItemId);
    this.updateReturnQuantity(item, (draft?.quantity ?? 0) - 1);
  }

  increaseReturnQuantity(item: SaleItemDto): void {
    const draft = this.getDraft(item.saleItemId);
    this.updateReturnQuantity(item, (draft?.quantity ?? 0) + 1);
  }

  getSelectedStatusLabel(): string {
    return `${this.selectedLineCount()} of ${this.getReturnableLineCount()}`;
  }

  getPolicyInventoryLine(): string {
    const firstGoods = this.returnableItems().find((item) => item.lineType === 'Goods');
    const itemName = firstGoods?.itemName ?? 'Returnable goods';
    return `${itemName} can return to stock if sealed.`;
  }

  getPolicyFinanceLine(): string {
    return 'Service refunds require a reason before recording.';
  }

  getPolicyReceiptLine(): string {
    return 'A return receipt will be generated after confirmation.';
  }

  getFooterNote(): string {
    return 'Preview the financial impact before recording. Only selected lines will affect stock, tax reversal, and customer balance.';
  }

  private getTaxReversalAmount(item: SaleItemDto, quantity: number): number {
    if (item.taxRatePercent <= 0 || quantity <= 0) {
      return 0;
    }

    const grossOriginalValue = quantity * item.salesPrice;
    const taxAmount = item.isPriceIncludingTax
      ? (grossOriginalValue * item.taxRatePercent) / (100 + item.taxRatePercent)
      : (grossOriginalValue * item.taxRatePercent) / 100;

    return this.roundMoney(taxAmount);
  }

  private getLatestCreditNoteSummary(): SaleReturnCreditNoteSummaryDto | null {
    const sale = this.salesFacade.selectedSale() ?? this.saleInput();
    const latestReturn = sale?.returns
      ?.filter((saleReturn) => !saleReturn.isVoided && saleReturn.creditNote)
      .slice()
      .sort(
        (left, right) =>
          new Date(right.returnedAt).getTime() - new Date(left.returnedAt).getTime(),
      )[0];

    return latestReturn?.creditNote ?? null;
  }

  private getCreditNotePrintUrl(code: string): string {
    return `/sales/credit-notes/${encodeURIComponent(code)}/print`;
  }

  private initializeDrafts(): void {
    const sale = this.saleInput();
    if (!sale) return;

    this.returnDrafts.set(
      sale.items.map((item) => ({
        saleItemId: item.saleItemId,
        selected: false,
        quantity: 0,
        condition: null,
        approvedRefundAmount: null,
        notes: '',
      })),
    );

    this.validationMessages.set([]);
    this.dueReductionOverrideAmount.set(null);
    this.dueOverrideReason.set('');
    this.dueOverrideConfirmed.set(false);
    this.payoutDestination.set(null);
    this.creditNoteExpiryMode.set('NoExpiry');
    this.creditNoteExpiryDate.set(null);
    this.salesFacade.clearSaleReturnPreview();
  }

  private updateDraft(
    saleItemId: string,
    update: (draft: ReturnLineDraft) => ReturnLineDraft,
  ): void {
    this.returnDrafts.update((drafts) =>
      drafts.map((draft) => (draft.saleItemId === saleItemId ? update(draft) : draft)),
    );
  }

  private validateReturnDrafts(): string[] {
    const sale = this.saleInput();
    const selectedDrafts = this.selectedDrafts();
    const errors: string[] = [];

    if (!sale || selectedDrafts.length === 0) {
      return ['Select at least one return line.'];
    }

    for (const draft of selectedDrafts) {
      const item = sale.items.find((line) => line.saleItemId === draft.saleItemId);
      const itemName = item?.itemName || 'Selected item';

      if (!item) {
        errors.push(`${itemName} is no longer available.`);
        continue;
      }

      if (draft.quantity <= 0) {
        errors.push(`Enter quantity for ${itemName}.`);
      }

      if (draft.quantity > item.returnableQuantity) {
        errors.push(`${itemName} exceeds returnable quantity.`);
      }

      if (!draft.condition && item.lineType !== 'Service') {
        errors.push(`Select condition for ${itemName}.`);
      }

      if (this.hasFinancialAccess() && draft.approvedRefundAmount === null) {
        errors.push(`Enter refund amount for ${itemName}.`);
      }
    }

    return errors;
  }

  private validateSubmit(): string[] {
    const errors = this.validateReturnDrafts();

    if (!this.canSubmitReturns()) {
      errors.push('You do not have permission to record returns.');
    }

    if (this.dueReductionOverrideAmount() !== null && !this.dueOverrideConfirmed()) {
      errors.push('Confirm due override before recording.');
    }

    const payoutAmount = this.returnPreview()?.financial?.payoutAmount ?? 0;
    if (payoutAmount > 0 && !this.payoutDestination()) {
      errors.push('Select payout destination.');
    }

    const isCreditNote =
      this.mapSelectedPayoutDestination(this.payoutDestination()) ===
      ReturnPayoutDestination.CreditNote;
    if (isCreditNote && this.creditNoteExpiryMode() === 'Custom') {
      const dateStr = this.creditNoteExpiryDate();
      if (!dateStr) {
        errors.push('Select an expiry date for custom credit note expiry.');
      } else {
        const selectedDate = new Date(`${dateStr}T00:00:00Z`);
        const today = new Date();
        today.setUTCHours(0, 0, 0, 0);
        if (selectedDate < today) {
          errors.push('Expiry date cannot be in the past.');
        }
      }
    }

    return errors;
  }

  private mapSelectedPayoutDestination(destination: number | null): ReturnPayoutDestination | null {
    return mapPayoutDestinationSelectionToReturnPayoutDestination(destination);
  }

  private clampNumber(value: number, min: number, max: number): number {
    if (!Number.isFinite(value)) return min;
    return Math.min(max, Math.max(min, value));
  }

  private normalizeOptional(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private roundMoney(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private getLineType(saleItemId: string): 'Goods' | 'Service' {
    return (
      this.saleInput()?.items.find((line) => line.saleItemId === saleItemId)?.lineType ?? 'Goods'
    );
  }

  private getRequestedCondition(
    saleItemId: string,
    condition: SaleReturnCondition | null,
  ): SaleReturnCondition | null {
    return this.getLineType(saleItemId) === 'Service' ? null : (condition ?? 1);
  }

  private computeCreditNoteExpiresAt(): string | null {
    const mode = this.creditNoteExpiryMode();
    if (mode === 'NoExpiry') return null;

    const today = new Date();
    let expiryDate: Date;

    if (mode === 'Custom') {
      const dateStr = this.creditNoteExpiryDate();
      if (!dateStr) return null;
      expiryDate = new Date(`${dateStr}T23:59:59Z`);
    } else {
      const days = mode === '30Days' ? 30 : mode === '60Days' ? 60 : 90;
      expiryDate = new Date(today);
      expiryDate.setDate(expiryDate.getDate() + days);
    }

    return expiryDate.toISOString();
  }
}
