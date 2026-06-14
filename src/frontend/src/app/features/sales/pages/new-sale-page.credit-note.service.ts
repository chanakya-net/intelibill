import { signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CreditNoteVerifyResponseDto } from '../services/sale.models';
import { NewSalePageOfflineFlowService } from './new-sale-page.offline-flow.service';

export abstract class NewSalePageCreditNoteService extends NewSalePageOfflineFlowService {
  readonly creditNoteCode = signal('');
  readonly isCreditNoteVerifying = signal(false);
  readonly verifiedCreditNote = signal<CreditNoteVerifyResponseDto | null>(null);
  readonly creditNoteError = signal('');

  onCreditNoteCodeChange(code: string): void {
    this.creditNoteCode.set(code ?? '');
    this.verifiedCreditNote.set(null);
    this.creditNoteError.set('');
    this.resetCreditNoteMismatchState();
  }

  async onVerifyCreditNote(): Promise<void> {
    const code = this.creditNoteCode().trim();
    if (!code) {
      return;
    }

    this.isCreditNoteVerifying.set(true);
    this.creditNoteError.set('');
    this.verifiedCreditNote.set(null);

    try {
      const result = await firstValueFrom(this.saleService.verifyCreditNote(code));
      this.verifiedCreditNote.set(result);
      this.refreshCreditNoteCustomerMismatchState();
    } catch {
      this.creditNoteError.set('sales.newSale.creditNote.verifyError');
    } finally {
      this.isCreditNoteVerifying.set(false);
    }
  }

  onCreditNoteCustomerMismatchConfirmedChange(confirmed: boolean): void {
    this.creditNoteCustomerMismatchConfirmed.set(confirmed);
  }

  onCreditNoteCustomerMismatchCancelled(): void {
    this.resetCreditNoteState();
  }

  override refreshCreditNoteCustomerMismatchState(): void {
    const warning = this.hasCreditNoteCustomerMismatch(this.verifiedCreditNote());
    this.creditNoteCustomerMismatchWarning.set(warning);
    this.creditNoteCustomerMismatchConfirmed.set(false);
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
    this.resetCreditNoteMismatchState();
  }

  private hasCreditNoteCustomerMismatch(creditNote: CreditNoteVerifyResponseDto | null): boolean {
    if (!creditNote) {
      return false;
    }

    // Name-based comparison used as fallback; API does not return customer ID for credit notes.
    const saleCustomerName = this.selectedCustomer()?.name.trim().toLowerCase();
    const creditNoteCustomerName = creditNote.customerName?.trim().toLowerCase();

    if (!saleCustomerName || !creditNoteCustomerName) {
      return false;
    }

    return saleCustomerName !== creditNoteCustomerName;
  }

  private resetCreditNoteMismatchState(): void {
    this.creditNoteCustomerMismatchWarning.set(false);
    this.creditNoteCustomerMismatchConfirmed.set(false);
  }
}
