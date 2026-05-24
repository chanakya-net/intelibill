import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { DividerModule } from 'primeng/divider';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../../core/auth/auth.service';
import { SalesFacade } from '../state/sales.facade';
import { PAYMENT_METHOD_VALUES, SALE_RETURN_CONDITIONS } from '../services/sale.models';
import type {
  PreviewSaleReturnRequest,
  RecordSaleReturnRequest,
  SaleItemDto,
  SaleReturnCondition,
  SaleReturnDto,
  VoidSaleReturnRequest,
} from '../services/sale.models';
import { SaleLineItemsTableComponent } from './sale-detail/sale-line-items-table.component';
import { SaleSummaryPanelComponent } from './sale-detail/sale-summary-panel.component';
interface ReturnLineDraft {
  readonly saleItemId: string;
  readonly selected: boolean;
  readonly quantity: number;
  readonly condition: SaleReturnCondition | null;
  readonly approvedRefundAmount: number | null;
  readonly notes: string;
}
@Component({
  selector: 'app-sale-detail-overlay',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    DividerModule,
    InputNumberModule,
    InputTextModule,
    ProgressSpinnerModule,
    SelectModule,
    TagModule,
    TableModule,
    TextareaModule,
    TranslocoPipe,
    SaleLineItemsTableComponent,
    SaleSummaryPanelComponent,
  ],
  templateUrl: './sale-detail-overlay.component.html',
})
export class SaleDetailOverlayComponent {
  private readonly salesFacade = inject(SalesFacade);
  private readonly authService = inject(AuthService);
  @Input() visible = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  readonly sale = this.salesFacade.selectedSale;
  readonly isLoading = this.salesFacade.loadingSaleDetail;
  readonly returnPreview = this.salesFacade.returnPreview;
  readonly isPreviewLoading = this.salesFacade.loadingReturnPreview;
  readonly isSubmitting = this.salesFacade.submitting;
  readonly previewError = this.salesFacade.returnPreviewErrorMessage;
  readonly showReturnPreview = signal(false);
  readonly returnDrafts = signal<readonly ReturnLineDraft[]>([]);
  readonly validationMessages = signal<readonly string[]>([]);
  readonly dueReductionOverrideAmount = signal<number | null>(null);
  readonly dueOverrideReason = signal('');
  readonly dueOverrideConfirmed = signal(false);
  readonly payoutMethod = signal<number | null>(null);
  readonly showVoidReturn = signal(false);
  readonly selectedReturnToVoid = signal<SaleReturnDto | null>(null);
  readonly voidReason = signal('');
  readonly returnConditionOptions = SALE_RETURN_CONDITIONS;
  readonly refundPayoutMethodOptions = PAYMENT_METHOD_VALUES.filter((method) => method.value !== 4);
  readonly activeShopRole = computed(() => {
    const session = this.authService.session();
    if (!session) return '';
    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ?? session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly returnableItems = computed(() => this.sale()?.items.filter((item) => item.returnableQuantity > 0) ?? []);
  readonly canPreviewReturns = computed(() => {
    const role = this.activeShopRole().toLowerCase();
    return ['owner', 'manager', 'staff'].includes(role) && this.returnableItems().length > 0;
  });
  readonly hasFinancialAccess = computed(() => ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()));
  readonly canSubmitReturns = this.hasFinancialAccess;
  readonly selectedDrafts = computed(() => this.returnDrafts().filter((draft) => draft.selected));
  constructor() {
    effect(() => {
      if (!this.salesFacade.lastMutationSucceeded()) return;
      if (this.salesFacade.lastMutationType() === 'record-return') {
        this.showReturnPreview.set(false);
        this.validationMessages.set([]);
        this.dueReductionOverrideAmount.set(null);
        this.dueOverrideReason.set('');
        this.dueOverrideConfirmed.set(false);
        this.payoutMethod.set(null);
        this.salesFacade.clearSaleReturnPreview();
        this.salesFacade.clearMutationStatus();
        return;
      }
      if (this.salesFacade.lastMutationType() === 'void-return') {
        this.closeVoidReturn();
        this.salesFacade.clearMutationStatus();
      }
    });
  }
  openReturnPreview(): void {
    const detail = this.sale();
    if (!detail || !this.canPreviewReturns()) return;
    this.returnDrafts.set(
      detail.items.map((item) => ({
        saleItemId: item.saleItemId, selected: false, quantity: 0,
        condition: null, approvedRefundAmount: null, notes: '',
      })),
    );
    this.validationMessages.set([]);
    this.dueReductionOverrideAmount.set(null);
    this.dueOverrideReason.set('');
    this.dueOverrideConfirmed.set(false);
    this.payoutMethod.set(null);
    this.salesFacade.clearSaleReturnPreview();
    this.showReturnPreview.set(true);
  }
  closeReturnPreview(): void {
    this.showReturnPreview.set(false); this.validationMessages.set([]);
    this.dueOverrideConfirmed.set(false); this.payoutMethod.set(null);
    this.salesFacade.clearSaleReturnPreview();
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
      condition: selected ? draft.condition : null,
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
  updateReturnCondition(item: SaleItemDto, condition: SaleReturnCondition | null): void { this.updateDraft(item.saleItemId, (draft) => ({ ...draft, condition })); this.salesFacade.clearSaleReturnPreview(); }
  updateRefundAmount(item: SaleItemDto, refundAmount: number | string | null): void {
    const draft = this.getDraft(item.saleItemId);
    const maxRefund = this.getMaxRefundAmount(item, draft?.quantity ?? item.returnableQuantity);
    this.updateDraft(item.saleItemId, (existing) => ({ ...existing, approvedRefundAmount: this.clampNumber(Number(refundAmount), 0, maxRefund) }));
    this.salesFacade.clearSaleReturnPreview();
  }
  updateNotes(item: SaleItemDto, notes: string): void { this.updateDraft(item.saleItemId, (draft) => ({ ...draft, notes })); this.salesFacade.clearSaleReturnPreview(); }
  updateDueReductionOverride(value: number | string | null): void { this.dueReductionOverrideAmount.set(value === '' || value === null ? null : this.clampNumber(Number(value), 0, Number.MAX_SAFE_INTEGER)); this.dueOverrideConfirmed.set(false); this.salesFacade.clearSaleReturnPreview(); }
  updateDueOverrideReason(value: string): void { this.dueOverrideReason.set(value); this.salesFacade.clearSaleReturnPreview(); }
  previewReturn(): void {
    const detail = this.sale();
    if (!detail) return;
    const errors = this.validateReturnDrafts();
    this.validationMessages.set(errors);
    if (errors.length > 0) {
      this.salesFacade.clearSaleReturnPreview();
      return;
    }
    const payload: PreviewSaleReturnRequest = {
      dueReductionOverrideAmount: this.hasFinancialAccess() ? this.dueReductionOverrideAmount() : null,
      dueOverrideReason: this.hasFinancialAccess() ? this.normalizeOptional(this.dueOverrideReason()) : null,
      items: this.selectedDrafts().map((draft) => ({
        saleItemId: draft.saleItemId, quantity: draft.quantity, condition: draft.condition!,
        approvedRefundAmount: this.hasFinancialAccess() ? draft.approvedRefundAmount : null,
        notes: this.normalizeOptional(draft.notes),
      })),
    };
    this.salesFacade.previewSaleReturn(detail.saleId, payload);
  }
  updatePayoutMethod(method: number | null): void { this.payoutMethod.set(method); }
  updateDueOverrideConfirmed(confirmed: boolean): void { this.dueOverrideConfirmed.set(confirmed); }
  submitReturn(): void {
    const detail = this.sale();
    if (!detail) return;
    const errors = this.validateSubmit();
    this.validationMessages.set(errors);
    if (errors.length > 0) return;
    const payload: RecordSaleReturnRequest = {
      payoutMethod: (this.returnPreview()?.financial?.payoutAmount ?? 0) > 0 ? this.payoutMethod() : null,
      dueReductionOverrideAmount: this.dueReductionOverrideAmount(),
      dueOverrideReason: this.normalizeOptional(this.dueOverrideReason()),
      notes: null,
      items: this.selectedDrafts().map((draft) => ({
        saleItemId: draft.saleItemId,
        quantity: draft.quantity,
        condition: draft.condition!,
        approvedRefundAmount: draft.approvedRefundAmount,
        notes: this.normalizeOptional(draft.notes),
      })),
    };
    this.salesFacade.recordSaleReturn(detail.saleId, payload);
  }
  openVoidReturn(saleReturn: SaleReturnDto): void {
    if (!this.canSubmitReturns() || saleReturn.isVoided) return;
    this.selectedReturnToVoid.set(saleReturn); this.voidReason.set('');
    this.validationMessages.set([]); this.salesFacade.clearSaleReturnPreview();
    this.showVoidReturn.set(true);
  }

  closeVoidReturn(): void { this.showVoidReturn.set(false); this.selectedReturnToVoid.set(null); this.voidReason.set(''); this.validationMessages.set([]); }
  updateVoidReason(reason: string): void { this.voidReason.set(reason); }
  submitVoidReturn(): void {
    const detail = this.sale();
    const saleReturn = this.selectedReturnToVoid();
    const reason = this.normalizeOptional(this.voidReason());
    if (!detail || !saleReturn) return;
    if (!reason) { this.validationMessages.set(['Void reason is required.']); return; }
    this.salesFacade.voidSaleReturn(detail.saleId, saleReturn.saleReturnId, { reason });
  }
  printA4(): void { const saleId = this.sale()?.saleId; if (saleId) window.open(`/sales/${saleId}/print?template=a4`, '_blank'); }
  printThermal(): void { const saleId = this.sale()?.saleId; if (saleId) window.open(`/sales/${saleId}/print?template=thermal`, '_blank'); }
  onClose(): void { this.closeReturnPreview(); this.closeVoidReturn(); this.visibleChange.emit(false); }
  getDraft(saleItemId: string): ReturnLineDraft | null { return this.returnDrafts().find((draft) => draft.saleItemId === saleItemId) ?? null; }
  isFullyReturned(item: SaleItemDto): boolean { return item.returnableQuantity <= 0; }
  getMaxRefundAmount(item: SaleItemDto, quantity: number): number {
    const grossOriginalValue = quantity * item.salesPrice;
    if (item.taxRatePercent <= 0 || item.isPriceIncludingTax) return this.roundMoney(grossOriginalValue);
    return this.roundMoney(grossOriginalValue + (grossOriginalValue * item.taxRatePercent) / 100);
  }
  getPreviewItemName(saleItemId: string): string { return this.sale()?.items.find((line) => line.saleItemId === saleItemId)?.itemName || 'Unknown Item'; }
  returnStatusLabel(saleReturn: SaleReturnDto): string { return saleReturn.isVoided ? 'Voided' : 'Active'; }
  returnStatusSeverity(saleReturn: SaleReturnDto): 'success' | 'info' | 'warn' | 'danger' | 'secondary' { return saleReturn.isVoided ? 'secondary' : 'success'; }
  private updateDraft(saleItemId: string, update: (draft: ReturnLineDraft) => ReturnLineDraft): void {
    this.returnDrafts.update((drafts) => drafts.map((draft) => draft.saleItemId === saleItemId ? update(draft) : draft));
  }
  private validateReturnDrafts(): string[] {
    const detail = this.sale();
    const selectedDrafts = this.selectedDrafts();
    const errors: string[] = [];
    if (!detail || selectedDrafts.length === 0) return ['Select at least one return line.'];
    for (const draft of selectedDrafts) {
      const item = detail.items.find((line) => line.saleItemId === draft.saleItemId);
      const itemName = item?.itemName || 'Selected item';
      if (!item || item.returnableQuantity <= 0) { errors.push(`${itemName} is fully returned.`); continue; }
      if (draft.quantity <= 0 || draft.quantity > item.returnableQuantity) errors.push(`${itemName} quantity must be between 1 and ${item.returnableQuantity}.`);
      if (!draft.condition) errors.push(`${itemName} condition is required.`);
      if (this.isLineNoteRequired(item, draft) && this.normalizeOptional(draft.notes) === null)
        errors.push(`${itemName} notes are required for wastage, partial refund, or zero refund.`);
    }
    return errors;
  }
  private validateSubmit(): string[] {
    const errors = this.validateReturnDrafts();
    const preview = this.returnPreview();
    const payoutAmount = preview?.financial?.payoutAmount ?? 0;
    const payoutMethod = this.payoutMethod();
    if (!this.canSubmitReturns()) errors.push('Only owners and managers can record returns.');
    if (!preview) errors.push('Preview the return before recording it.');
    if (payoutAmount > 0 && payoutMethod === null) errors.push('Select a refund payout method.');
    if (payoutMethod === 4) errors.push('Credit cannot be used as a refund payout method.');
    if (this.dueReductionOverrideAmount() !== null) {
      if (!this.dueOverrideConfirmed()) errors.push('Confirm the due override before recording the return.');
      if (this.normalizeOptional(this.dueOverrideReason()) === null) errors.push('Enter a reason for the due override.');
    }
    return errors;
  }
  private isLineNoteRequired(item: SaleItemDto, draft: ReturnLineDraft): boolean {
    if (!draft.selected) return false;
    if (draft.condition === 2) return true;
    if (!this.hasFinancialAccess() || draft.approvedRefundAmount === null) return false;
    return draft.approvedRefundAmount === 0 || draft.approvedRefundAmount < this.getMaxRefundAmount(item, draft.quantity);
  }
  private clampNumber(value: number, min: number, max: number): number {
    if (!Number.isFinite(value)) return min;
    return this.roundMoney(Math.min(max, Math.max(min, value)));
  }
  private roundMoney(value: number): number { return Math.round((value + Number.EPSILON) * 100) / 100; }
  private normalizeOptional(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
