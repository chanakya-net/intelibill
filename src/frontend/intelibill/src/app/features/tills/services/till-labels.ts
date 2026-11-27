/** Known backend purpose codes have localized business labels; future codes remain secondary references. */
const PURPOSE_LABELS: Readonly<Record<string, string>> = {
  sale: 'sales.profitLoss.rowTypes.Sale',
  payment: 'sales.history.payment',
  refund: 'sales.history.amount.refund',
  'cash-refund': 'tills.purposes.cashRefund',
  settlement: 'tills.purposes.recordedSettlement',
  'cash-in': 'tills.cashIn',
  'cash-out': 'tills.cashOut',
};
export function tillPurposeLabel(purpose: string): string {
  return PURPOSE_LABELS[purpose] ?? 'tills.sourceAudit';
}
export function tillEvidenceLabel(status: string): string {
  return status === 'Recorded' ? 'tills.recorded' : 'shops.unknown';
}
