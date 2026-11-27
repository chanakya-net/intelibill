export type ExpiryMode = 'Ignore' | 'Warn' | 'Block';
export interface ExpiryPolicy {
  readonly version: number;
  readonly mode: ExpiryMode;
  readonly expiryRequired: boolean;
  readonly sellThroughExpiryDate: boolean;
  readonly warningDays: number;
  readonly overrideAllowed: boolean;
}
export type ExpiryPolicySettings = Omit<ExpiryPolicy, 'version'>;
export interface ExpiryBatchFacts {
  readonly batchId: string;
  readonly itemId: string;
  readonly expiryDate?: string | null;
  readonly manufacturingDate?: string | null;
  readonly physicalQuantity: number;
  readonly quarantinedQuantity: number;
  readonly isVoided: boolean;
  readonly itemActive: boolean;
  readonly batchVersion: number;
}
export interface SellabilityDecision {
  readonly allowed: boolean;
  readonly reasonCode: string | null;
  readonly warning: boolean;
  readonly requiresApproval: boolean;
  readonly policyVersion: number;
  readonly catalogVersion: number;
  readonly businessDate: string;
  readonly physicalQuantity: number;
  readonly quarantinedQuantity: number;
  readonly availableQuantity: number;
}
export interface ExpiryPolicyState {
  readonly defaultPolicy: ExpiryPolicy;
  readonly overridePolicy: ExpiryPolicy | null;
  readonly effectivePolicy: ExpiryPolicy;
  readonly catalogVersion: number;
}
export interface ExpiryPolicyUpdate {
  readonly expectedCatalogVersion: number;
  readonly policy: ExpiryPolicySettings | null;
  readonly reason: string;
}
export interface ExpiryApproval {
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly batchVersion: number;
  readonly policyVersion: number;
  readonly catalogVersion: number;
  readonly reason: string;
}
export interface SaleExpiryLineDecision {
  readonly itemId: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly batchVersion: number;
  readonly decision: SellabilityDecision;
}
export interface OfflineExpiryLineEvidence {
  readonly evidenceId: string;
  readonly catalogVersion: number;
  readonly policyVersion: number;
  readonly businessDate: string;
  readonly expiryDate: string | null;
  readonly decisionCode: string | null;
}
export function isExpiryPolicy(value: unknown): value is ExpiryPolicy {
  const p = value as ExpiryPolicy | null;
  return (
    !!p &&
    Number.isSafeInteger(p.version) &&
    p.version > 0 &&
    ['Ignore', 'Warn', 'Block'].includes(p.mode) &&
    typeof p.expiryRequired === 'boolean' &&
    typeof p.sellThroughExpiryDate === 'boolean' &&
    Number.isInteger(p.warningDays) &&
    p.warningDays >= 0 &&
    p.warningDays <= 365 &&
    typeof p.overrideAllowed === 'boolean'
  );
}

export interface SavedSaleExpiryDecision {
  readonly saleId: string;
  readonly itemId: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly policy: ExpiryPolicy;
  readonly catalogVersion: number;
  readonly batchVersion: number;
  readonly businessDate: string;
  readonly reasonCode: string | null;
  readonly warning: boolean;
  readonly approvedBy: string | null;
  readonly approvalReason: string | null;
  readonly approvedAt: string | null;
}
