import { HttpErrorResponse } from '@angular/common/http';
import { problemCode } from '@intelibill/api';
import { InvoiceInputError } from './billing-document-file';

export type AuthorityLoss = 'capability' | 'session';

export const definiteRefusals = new Map<string, string>([
  ['BillingDocument.Invalid', 'Check the invoice metadata, correction reason and issue date.'],
  ['BillingDocument.ContentInvalid', 'Select a valid PDF.'],
  ['BillingDocument.ContentMismatch', 'The PDF differs from the reviewed file.'],
  ['BillingDocument.TooLarge', 'PDFs must be no larger than 10 MiB.'],
  ['BillingDocument.MediaTypeUnsupported', 'Select a PDF file.'],
  ['BillingDocument.OrderNotFound', 'The accepted order was not found for this account.'],
  ['BillingDocument.PredecessorNotFound', 'The invoice version to supersede was not found for this order.'],
  ['BillingDocument.AlreadyAttached', 'An accounting invoice already exists for this order.'],
  ['BillingDocument.PredecessorStale', 'A newer invoice version already exists. Review the current invoice again.'],
  ['BillingDocument.PreviewStale', 'Review the order and invoice again before attaching.'],
]);

export const reloadCodes = new Set([
  'BillingDocument.AlreadyAttached',
  'BillingDocument.PredecessorStale',
  'BillingDocument.PredecessorNotFound',
]);

export const CLEANUP_FAILED_MESSAGE = 'Local draft cleanup could not finish. Retry refreshing invoice history.';

export function errorCode(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const direct = problemCode(error.error);
  if (direct) return direct;
  if (typeof error.error === 'string') {
    try {
      return problemCode(JSON.parse(error.error));
    } catch {
      return null;
    }
  }
  return null;
}

export function isReloadRefusal(error: unknown): boolean {
  const code = errorCode(error);
  return code !== null && reloadCodes.has(code);
}

export function isRecentMfaRequired(error: unknown): boolean {
  return errorCode(error) === 'ManualPayment.RecentMfaRequired';
}

export function classifyAuthorityLoss(error: unknown): AuthorityLoss | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const code = errorCode(error);
  if (error.status === 401 || code === 'Admin.SessionEnded') return 'session';
  if (error.status === 403 && (code === 'ManualPayment.AccessDenied' || code === 'Admin.AccessDenied' || !code))
    return 'capability';
  return null;
}

export function authorityLossMessage(loss: AuthorityLoss): string {
  return loss === 'session'
    ? 'Your provider session ended. Sign in with MFA to continue.'
    : 'Billing capability is required.';
}

export function formatRefusalMessage(error: unknown, hasPending: boolean, recentMfa: boolean): string {
  if (recentMfa) return 'Sign in with MFA to continue this upload.';
  if (hasPending) return 'The upload is unresolved. Check or retry the saved operation.';
  if (error instanceof InvoiceInputError) return error.message;
  return definiteRefusals.get(errorCode(error) ?? '') ?? 'The invoice request could not be completed.';
}

export function formatHistoryRefreshMessage(recentMfa: boolean): string {
  return recentMfa
    ? 'Sign in with MFA to refresh invoice history.'
    : 'Version history could not be refreshed. Retry loading history.';
}

export function formatInvoiceMoney(amountPaise: number, currency: string): string {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency }).format(amountPaise / 100);
}

export function assertOrderSnapshotUnchanged(
  current: { readonly snapshotHash: string; readonly revision: number } | undefined,
  next: { readonly snapshotHash: string; readonly revision: number },
) {
  if (next.snapshotHash !== current?.snapshotHash || next.revision !== current.revision)
    throw new InvoiceInputError('The order changed. Load the order and review the invoice again.');
}
