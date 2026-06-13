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

  resetCreditNoteState(): void {
    this.creditNoteCode.set('');
    this.isCreditNoteVerifying.set(false);
    this.verifiedCreditNote.set(null);
    this.creditNoteError.set('');
  }
}
