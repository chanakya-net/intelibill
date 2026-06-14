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
  }

  override onApplyVerifiedCreditNote(): void {
    const verified = this.verifiedCreditNote();
    if (!verified || this.hasAppliedCreditNoteCode(verified.code)) {
      return;
    }

    const amount = this.roundAmount(Math.max(0, Math.min(verified.availableBalance, this.remainingPayableAmount())));
    if (amount <= 0) {
      return;
    }

    this.addAppliedCreditNote({
      creditNoteId: verified.creditNoteId,
      code: verified.code,
      availableBalance: verified.availableBalance,
      expiresAt: verified.expiresAt,
      status: verified.status,
      amount,
    });
    this.verifiedCreditNote.set(null);
    this.creditNoteCode.set('');
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
    } catch {
      this.creditNoteError.set('sales.newSale.creditNote.verifyError');
    } finally {
      this.isCreditNoteVerifying.set(false);
    }
  }

  override resetTransientState(): void {
    super.resetTransientState();
    this.creditNoteCode.set('');
    this.isCreditNoteVerifying.set(false);
    this.verifiedCreditNote.set(null);
    this.creditNoteError.set('');
    this.clearAppliedCreditNotes();
  }
}
