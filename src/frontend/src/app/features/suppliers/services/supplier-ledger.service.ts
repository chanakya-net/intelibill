import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SUPPLIER_ENDPOINTS } from '../../../core/auth/auth.constants';

export type SupplierLedgerEntryKind =
  | 'GoodsReceived'
  | 'PaymentMade'
  | 'RecordAdjusted'
  | 'Reversal'
  | 'CostAdjusted';

export interface SupplierLedgerEntry {
  readonly id: string;
  readonly supplierId: string;
  /** The API sends the enum name (`GoodsReceived`); older payloads used numbers or `GOODS_RECEIVED`. */
  readonly entryType: SupplierLedgerEntryKind | string | number;
  readonly amount: number;
  readonly entryDate: string;
  readonly notes: string | null;
  /** The payment receipt behind a `PaymentMade` entry. */
  readonly receiptId?: string | null;
}

export interface MakePaymentRequest {
  readonly amount: number;
  readonly paymentDate: string;
  readonly notes: string | null;
  /** How the money moved; absent means not recorded (the API never assumes cash). */
  readonly method?: 'Cash' | 'Upi' | 'Card' | 'BankTransfer' | 'Cheque' | null;
  readonly externalReference?: string | null;
  /** Purchase receipts (`purchaseReceiptValueId`) this payment settles. */
  readonly allocations?: readonly { readonly targetId: string; readonly amount: number }[];
}

export interface KeyedMakePaymentRequest extends MakePaymentRequest {
  readonly operationKey: string;
}

@Injectable({
  providedIn: 'root',
})
export class SupplierLedgerService {
  private readonly http = inject(HttpClient);

  getSupplierLedgerEntries(supplierId: string): Observable<readonly SupplierLedgerEntry[]> {
    return this.http.get<readonly SupplierLedgerEntry[]>(
      `${SUPPLIER_ENDPOINTS.update(supplierId)}/ledger`,
    );
  }

  makePayment(
    supplierId: string,
    payload: KeyedMakePaymentRequest,
  ): Observable<SupplierLedgerEntry> {
    return this.http.post<SupplierLedgerEntry>(SUPPLIER_ENDPOINTS.payments(supplierId), payload);
  }
}

const ENTRY_KINDS: readonly SupplierLedgerEntryKind[] = [
  'GoodsReceived',
  'PaymentMade',
  'RecordAdjusted',
  'Reversal',
  'CostAdjusted',
];

/** Reads any wire form of a ledger entry type (enum name, SNAKE_CASE or number). */
export function supplierLedgerEntryKind(
  entryType: SupplierLedgerEntry['entryType'],
): SupplierLedgerEntryKind | null {
  if (typeof entryType === 'number') return ENTRY_KINDS[entryType - 1] ?? null;
  const compact = entryType.replace(/_/g, '').toLowerCase();
  return ENTRY_KINDS.find((kind) => kind.toLowerCase() === compact) ?? null;
}
