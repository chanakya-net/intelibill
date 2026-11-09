import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import type {
  ReceivePurchaseOrderLineRequest,
  ReceivePurchaseOrderRequest,
} from './purchase-order.service';

export interface ReceivingCorrelation {
  readonly scope: ShopScope;
  readonly purchaseOrderId: string;
  readonly operationId: string;
}
export interface ReceivingRowError {
  readonly clientLineId?: string;
  readonly field?: string;
  readonly code: string;
  readonly messageKey: string;
}
// InputNumber can hold null while the operator is editing. Dates stay local date-only strings.
export type ReceivingEditableLine = Omit<
  ReceivePurchaseOrderLineRequest,
  'clientLineId' | 'quantity' | 'totalPurchaseCost' | 'mrp' | 'salesPrice' | 'taxRatePercent'
> & {
  readonly clientLineId: string;
  readonly quantity: number | null;
  readonly totalPurchaseCost: number | null;
  readonly mrp: number | null;
  readonly salesPrice: number | null;
  readonly taxRatePercent: number | null;
};
export interface ReceivingEditableDraft {
  readonly referenceNumber: string;
  readonly notes: string;
  readonly receivedAt: string | null;
  readonly lines: readonly ReceivingEditableLine[];
}
export interface ReceivingDraftRecord {
  readonly schemaVersion: 1;
  readonly userId: string;
  readonly shopId: string;
  readonly purchaseOrderId: string;
  readonly revision: number;
  readonly draft: ReceivingEditableDraft;
  readonly pending: null | {
    readonly operationId: string;
    readonly payload: ReceivePurchaseOrderRequest;
    readonly status: 'sending' | 'unknown';
  };
  readonly confirmedOperationId: string | null;
  readonly updatedAt: string;
}
export interface ReceivingCommand {
  readonly correlation: ReceivingCorrelation;
  readonly payload: ReceivePurchaseOrderRequest;
  readonly recovery: boolean;
}
export interface ReceivingOperationState {
  readonly correlation: ReceivingCorrelation;
  readonly status: 'sending' | 'checking' | 'confirmed' | 'rejected' | 'unknown';
  readonly localizedError?: LocalizedError;
  readonly errorMessage: string;
  readonly rowErrors: readonly ReceivingRowError[];
}
export function receivingKey(c: ReceivingCorrelation): string {
  return JSON.stringify([
    c.scope.userId,
    c.scope.shopId,
    c.scope.epoch,
    c.purchaseOrderId,
    c.operationId,
  ]);
}
