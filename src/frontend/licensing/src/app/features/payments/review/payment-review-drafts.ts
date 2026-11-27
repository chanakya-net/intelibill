import { Injectable } from '@angular/core';
import { PaymentDraftStore } from '../manual-payment-drafts';
import type { ReviewCommand } from './payment-review-api.service';

@Injectable({ providedIn: 'root' })
export class PaymentReviewDrafts extends PaymentDraftStore<ReviewCommand> {
  constructor() {
    super('intelibill.admin.payment-reviews');
  }
}
