import type { RefundTreatment } from './refund-approval-api.service';

export const TREATMENT_CHOICES: readonly { readonly value: RefundTreatment; readonly label: string; readonly detail: string }[] = [
  { value: 'Preserve', label: 'Preserve access',
    detail: 'Record the refund only. No shop, term, member or restriction changes.' },
  { value: 'EndAt', label: 'End remaining access at a stated instant',
    detail: 'The named shops keep access until that instant and nothing after it, grace included.' },
  { value: 'TermAdjustment', label: 'Adjust the paid-through date',
    detail: 'Replace the term’s paid-through boundary with the stated instant; ordinary rules follow from it.' },
];

/** Typed refusals that prove the frozen command was not recorded. */
export const RELEASING_REFUSALS: ReadonlySet<string> = new Set([
  'Refund.PreviewStale', 'Refund.AmountExceedsRemaining', 'Refund.AccessTreatmentUnavailable', 'Refund.ApprovalInvalid',
  'Refund.AlreadyDecided', 'Refund.RequestNotFound',
]);

const reasons: Readonly<Record<string, string>> = {
  'Refund.PreviewStale': 'the payment or its access changed after the preview',
  'Refund.AmountExceedsRemaining': 'the amount exceeds the request or what remains refundable on the verified payment',
  'Refund.AccessTreatmentUnavailable': 'this access treatment cannot apply to the payment’s current term at that instant',
  'Refund.ApprovalInvalid': 'the amount, reason or access treatment is incomplete',
  'Refund.AlreadyDecided': 'this refund request already has a decision',
  'Refund.RequestNotFound': 'this refund request is unavailable',
};

/** Whole paise from a rupee amount with at most two decimals; null for anything else or zero. */
export function parseRupees(value: string): number | null {
  const match = /^(\d{1,10})(?:\.(\d{1,2}))?$/.exec(value.trim());
  if (!match) return null;
  const paise = Number(match[1]) * 100 + Number((match[2] ?? '').padEnd(2, '0'));
  return paise > 0 ? paise : null;
}

/** An India Standard Time wall-clock entry (<c>YYYY-MM-DDTHH:mm[:ss]</c>) as an exact UTC instant. */
export function effectiveInstant(value: string): string | null {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2})(:\d{2})?$/.exec(value.trim());
  if (!match) return null;
  const parsed = Date.parse(`${match[1]}${match[2] ?? ':00'}+05:30`);
  return Number.isFinite(parsed) ? new Date(parsed).toISOString() : null;
}

export function refusalMessage(code: string | null, stillPending: boolean, wasPending: boolean): string {
  if (code === 'ManualPayment.RecentMfaRequired') return 'Sign in with MFA again to continue. The reviewed approval is kept.';
  if (code === 'ManualPayment.AccessDenied') return 'Billing access is required to approve refunds.';
  if (stillPending) return code === 'Refund.OperationNotFound'
    ? 'No approval is recorded under this key yet. The original key is kept: retry the exact approval or check again.'
    : 'The approval outcome is unknown. Check or retry the original approval; a new key is never created.';
  const reason = code ? reasons[code] : undefined;
  if (wasPending && reason) return `The approval was not recorded: ${reason}. Review the current preview and approve again.`;
  return reason ? `Not available: ${reason}.` : 'Refund approval is unavailable. Reload this request to try again.';
}
