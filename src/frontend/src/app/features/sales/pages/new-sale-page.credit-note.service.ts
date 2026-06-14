import { signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CreditNoteVerifyResponseDto } from '../services/sale.models';
import { NewSalePageOfflineFlowService } from './new-sale-page.offline-flow.service';

export abstract class NewSalePageCreditNoteService extends NewSalePageOfflineFlowService {
  readonly creditNoteCode = signal('');
  readonly isCreditNoteVerifying = signal(false);
  readonly verifiedCreditNote = signal<CreditNoteVerifyResponseDto | null>(null);
  readonly creditNoteError = signal('');
  readonly appliedCreditNoteAmount = signal(0);

  get maxAppliedCreditNoteAmount(): number {
    const note = this.verifiedCreditNote();
    if (!note) {
      return 0;
    }

    const verifiedBalance = Math.max(0, Number(note.availableBalance ?? 0));
    const paidAmount = this.toFiniteAmount(this.paymentForm.controls.paidAmount.value);
    const dueAmount = this.toFiniteAmount(this.paymentForm.controls.dueAmount.value);
    const remainingTotal = this.roundAmount(Math.max(0, this.totalAmount() - paidAmount - dueAmount));
    return this.roundAmount(Math.min(verifiedBalance, remainingTotal));
  }

  onCreditNoteCodeChange(code: string): void {
    this.creditNoteCode.set(code ?? '');
    this.verifiedCreditNote.set(null);
    this.appliedCreditNoteAmount.set(0);
    this.creditNoteError.set('');
  }

  async onVerifyCreditNote(): Promise<void> {
    const code = this.creditNoteCode().trim();
    if (!code) {
      return;
    }

    this.isCreditNoteVerifying.set(true);
    this.creditNoteError.set('');
    this.verifiedCreditNote.set(null);
    this.appliedCreditNoteAmount.set(0);

    try {
      const result = await firstValueFrom(this.saleService.verifyCreditNote(code));
      this.verifiedCreditNote.set(result);
      this.applyDefaultCreditNoteAmount();
    } catch {
      this.creditNoteError.set('sales.newSale.creditNote.verifyError');
    } finally {
      this.isCreditNoteVerifying.set(false);
    }
  }

  onCreditNoteAppliedAmountChange(value: number | null): void {
    const parsed = Number(value ?? 0);
    const normalized = Number.isFinite(parsed) ? this.roundAmount(Math.max(0, parsed)) : 0;
    const maxAmount = this.maxAppliedCreditNoteAmount;
    const nextAmount = this.roundAmount(Math.min(normalized, maxAmount));

    if (!Number.isFinite(nextAmount)) {
      this.appliedCreditNoteAmount.set(0);
      this.resetPaymentSplitAfterCreditNoteChange();
      return;
    }

    if (!this.areAmountsEqual(this.appliedCreditNoteAmount(), nextAmount)) {
      this.appliedCreditNoteAmount.set(nextAmount);
    }

    this.resetPaymentSplitAfterCreditNoteChange();
  }

  onCreditNoteRemovalRequested(): void {
    if (!this.appliedCreditNoteAmount()) {
      return;
    }

    this.appliedCreditNoteAmount.set(0);
    this.resetPaymentSplitAfterCreditNoteChange();
  }

  protected override getCreditNoteRedemptions(): { code: string; amount: number }[] {
    const note = this.verifiedCreditNote();
    const amount = this.appliedCreditNoteAmount();
    if (!note || !amount || !Number.isFinite(amount) || amount <= 0) {
      return [];
    }
    return [{ code: note.code, amount }];
  }

  protected override getCreditNoteAppliedAmount(): number {
    return this.appliedCreditNoteAmount();
  }

  protected applyDefaultCreditNoteAmount(): void {
    const amount = this.maxAppliedCreditNoteAmount;
    this.appliedCreditNoteAmount.set(amount);
    this.resetPaymentSplitAfterCreditNoteChange();
  }

  private resetPaymentSplitAfterCreditNoteChange(): void {
    if (this.lastEditedPaymentField() === 'due') {
      this.syncPaymentSplitFromDue(this.paymentForm.controls.dueAmount.value, this.totalAmount());
      return;
    }

    this.syncPaymentSplitFromPaid(this.paymentForm.controls.paidAmount.value, this.totalAmount());
  }

  override resetTransientState(): void {
    super.resetTransientState();
    this.resetCreditNoteState();
  }

  private resetCreditNoteState(): void {
    this.creditNoteCode.set('');
    this.isCreditNoteVerifying.set(false);
    this.verifiedCreditNote.set(null);
    this.creditNoteError.set('');
    this.appliedCreditNoteAmount.set(0);
  }
}
