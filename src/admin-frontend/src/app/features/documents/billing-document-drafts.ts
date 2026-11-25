import { Injectable } from '@angular/core';
import { PaymentDraftStore } from '../payments/manual-payment-drafts';
import type { InvoiceIntent } from './billing-document-file';

@Injectable({ providedIn: 'root' })
export class BillingDocumentDrafts extends PaymentDraftStore<InvoiceIntent> {
  constructor() {
    super('intelibill.admin.billing-documents');
  }
}
