export interface RefundOutcome {
  readonly approvedAmountPaise?: number | null;
  readonly providerState?: 'Reserved' | 'Pending' | 'Unknown' | 'Confirmed' | 'Failed' | 'Manual' | 'Historical' | null;
  readonly accessDisposition?: 'Applied' | 'NeedsReview' | 'AwaitingConfirmation' | null;
}

export function validRefundOutcome(row: Record<string, unknown>): boolean {
  const state = row['providerState'];
  const amount = row['approvedAmountPaise'];
  const disposition = row['accessDisposition'];
  if (amount == null && state == null && disposition == null) return true;
  if (typeof amount !== 'number' || !Number.isSafeInteger(amount) || amount <= 0 ||
      typeof row['amountPaise'] !== 'number' || amount > row['amountPaise']) return false;
  switch (state) {
    case 'Manual': case 'Historical': return row['status'] === 'Pending' && disposition === 'Applied';
    case 'Reserved': case 'Pending': return row['status'] === 'Pending' && disposition === 'AwaitingConfirmation';
    case 'Failed': case 'Unknown': return row['status'] === state && disposition === 'AwaitingConfirmation';
    case 'Confirmed': return row['status'] === state && (disposition === 'Applied' || disposition === 'NeedsReview');
    default: return false;
  }
}
