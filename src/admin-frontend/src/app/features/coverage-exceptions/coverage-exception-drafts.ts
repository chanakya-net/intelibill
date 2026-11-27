import { Injectable } from '@angular/core';
import { PaymentDraftStore } from '../payments/manual-payment-drafts';
import type { ApproveCoverageExceptionCommand } from './coverage-exception-api.service';

/**
 * Durable owner-bound approval drafts: one frozen command per admin + account + exceptionId so reload,
 * timeout, or recent-MFA re-authentication always checks or retries the original operation key.
 */
@Injectable({ providedIn: 'root' })
export class CoverageExceptionDrafts extends PaymentDraftStore<ApproveCoverageExceptionCommand> {
  constructor() {
    super('intelibill.admin.coverage-exceptions');
  }
}
