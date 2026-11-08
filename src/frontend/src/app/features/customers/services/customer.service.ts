import { TillSourceService, TillSourceFields } from '../../tills/services/till-source.service';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';

/**
 * How new due is judged against the saved credit limit. `LegacyAdvisory` is a setting recorded
 * before credit policies existed: shown, never enforced, until an owner or manager chooses.
 */
export type CreditPolicyMode = 'NoCredit' | 'Enforced' | 'LegacyAdvisory';

export interface Customer {
  customerId: string;
  name: string;
  phoneNumber: string;
  address: string | null;
  isActive: boolean;
  creditLimit: number;
  purchaseCount: number;
  lifetimeRevenue: number;
  currentMonthRevenue: number;
  outstandingDue?: number;
  /** A registered buyer's GSTIN; its first two digits are the customer's GST state. */
  gstin?: string | null;
  /** GST state code; absent only from responses that predate customer GST details. */
  stateCode?: string | null;
  /** Absent only from responses that predate credit policies (treat as legacy advisory). */
  creditPolicyMode?: CreditPolicyMode;
  /** Agreed days to pay a due sale; null when no terms were agreed. */
  creditTermsDays?: number | null;
}

export interface AddCustomerRequest {
  name: string;
  phoneNumber: string;
  address: string | null;
  isActive: boolean;
  creditLimit: number;
  gstin: string | null;
  stateCode: string | null;
  /** The web app always sends a policy; the API labels an absent one legacy advisory. */
  creditPolicyMode?: CreditPolicyMode;
  creditTermsDays?: number | null;
}

export interface EditCustomerRequest {
  name: string;
  phoneNumber: string;
  address: string | null;
  isActive: boolean;
  creditLimit: number;
  /** An empty string clears the recorded GSTIN; null keeps it. */
  gstin: string | null;
  /** An empty string clears the recorded state; null keeps it. */
  stateCode: string | null;
  /** With terms: both are saved as sent (null terms = none). Absent keeps the stored policy and terms. */
  creditPolicyMode?: CreditPolicyMode;
  creditTermsDays?: number | null;
}

export interface CustomerAccountSale {
  status?: 'Posted' | 'Cancelled';
  cancellationCreditAmount?: number;
  saleId: string;
  invoiceNumber: string;
  paymentMethod: number | string;
  soldAt: string;
  paidAmount: number;
  /** The due printed when the invoice was issued; it never changes. */
  dueAmount: number;
  totalAmount: number;
  /** Payments allocated to this invoice (absent from responses older than payment receipts). */
  allocatedPaymentAmount?: number;
  /** Due reduced by returns of this invoice. */
  returnCreditAmount?: number;
  /** What is still owed on this invoice today. */
  currentOutstandingAmount?: number;
  /** When the due falls due, fixed at issue from agreed terms; null = no terms (outstanding, never overdue). */
  dueDate?: string | null;
  /** True only when something is still owed and the due date has passed. */
  isOverdue?: boolean;
}

/** How the money moved, as the API names it. `UnknownRecordedMethod` = not recorded (legacy). */
export type ReceiptTenderMethod =
  | 'Cash'
  | 'Upi'
  | 'Card'
  | 'BankTransfer'
  | 'Cheque'
  | 'UnknownRecordedMethod';

export const RECORDABLE_TENDER_METHODS: readonly ReceiptTenderMethod[] = [
  'Cash',
  'Upi',
  'Card',
  'BankTransfer',
  'Cheque',
];

export type CustomerLedgerEntryKind =
  | 'SaleDue'
  | 'PaymentReceived'
  | 'ReturnCredit'
  | 'ReturnCreditReversal'
  | 'PaymentReversal'
  | 'SaleCancellationCredit';

const LEDGER_ENTRY_KINDS: readonly CustomerLedgerEntryKind[] = [
  'SaleDue',
  'PaymentReceived',
  'ReturnCredit',
  'ReturnCreditReversal',
  'PaymentReversal',
  'SaleCancellationCredit',
];

export interface CustomerLedgerEntry {
  entryId: string;
  saleId: string | null;
  /** The API sends the enum name (`PaymentReceived`); older payloads used its number. */
  entryType: number | string;
  amount: number;
  entryDate: string;
  notes: string | null;
  runningBalance: number;
  /** The payment receipt behind a `PaymentReceived` entry. */
  receiptId?: string | null;
  /** The entry's effect on what is owed: positive = debit, negative = credit. */
  signedAmount?: number;
}

/** Reads any wire form of a customer ledger entry type (enum name or number). */
export function customerLedgerEntryKind(
  entryType: CustomerLedgerEntry['entryType'],
): CustomerLedgerEntryKind | null {
  if (typeof entryType === 'number') return LEDGER_ENTRY_KINDS[entryType - 1] ?? null;
  const compact = entryType.replace(/_/g, '').toLowerCase();
  return LEDGER_ENTRY_KINDS.find((kind) => kind.toLowerCase() === compact) ?? null;
}

/** Whether the entry increases what the customer owes. */
export function isCustomerDebit(entryType: CustomerLedgerEntry['entryType']): boolean {
  const kind = customerLedgerEntryKind(entryType);
  return kind === 'SaleDue' || kind === 'ReturnCreditReversal' || kind === 'PaymentReversal';
}

export interface PaymentReceiptSummary {
  receiptId: string;
  amount: number;
  method: ReceiptTenderMethod;
  externalReference: string | null;
  businessDate: string;
  isLegacy: boolean;
  isReversed: boolean;
  allocatedAmount: number;
  unallocatedAmount: number;
  ledgerEntryId: string;
}

export interface CustomerAccount {
  customerId: string;
  name: string;
  phoneNumber: string;
  outstandingDue: number;
  sales: CustomerAccountSale[];
  ledgerEntries: CustomerLedgerEntry[];
  paymentHistory: CustomerLedgerEntry[];
  /** Payment money not applied to any invoice yet. */
  unallocatedCredit?: number;
  receipts?: PaymentReceiptSummary[];
  /** The saved credit limit, exactly as configured. */
  creditLimit?: number;
  isActive?: boolean;
  creditPolicyMode?: CreditPolicyMode;
  creditTermsDays?: number | null;
  /** New due the account can take now under an enforced policy; null when the limit is advisory. */
  availableCredit?: number | null;
  /** The balance before the statement window (0 when it starts at the beginning). */
  openingBalance?: number;
  /** Opening balance plus the window's signed entries. */
  closingBalance?: number;
  /** The business date overdue is judged on. */
  asOfBusinessDate?: string;
  /** Outstanding on invoices whose due date has passed. */
  overdueAmount?: number;
}

/** The checkout's advisory credit preview; the server decides again when the sale posts. */
export interface CustomerCreditStatus {
  customerId: string;
  isActive: boolean;
  creditPolicyMode: CreditPolicyMode;
  creditLimit: number;
  creditTermsDays: number | null;
  /** The signed account balance now (negative = the customer is in credit). */
  currentExposure: number;
  /** New due the account can take now; null when the limit is advisory (not enforced). */
  availableCredit: number | null;
}

export interface PaymentAllocationRequest {
  targetId: string;
  amount: number;
}

export interface RecordCustomerPaymentRequest extends TillSourceFields {
  amount: number;
  paymentDate: string;
  notes: string | null;
  method?: ReceiptTenderMethod | null;
  externalReference?: string | null;
  /** Invoices this payment settles; the rest stays visible as unallocated credit. */
  allocations?: PaymentAllocationRequest[];
}

export interface PaymentReceiptDetail {
  receiptId: string;
  partyType: 'Customer' | 'Supplier';
  partyId: string;
  partyName: string | null;
  amount: number;
  method: ReceiptTenderMethod;
  externalReference: string | null;
  businessDate: string;
  receivedAt: string;
  notes: string | null;
  isLegacy: boolean;
  createdByName: string | null;
  allocatedAmount: number;
  unallocatedAmount: number;
  allocations: {
    allocationId: string;
    targetId: string;
    targetLabel: string | null;
    amount: number;
  }[];
  reversal: {
    settlementEvidence?: import('../../tills/services/till.models').TillSettlementEvidence | null;
    reversalId: string;
    amount: number;
    reason: string;
    businessDate: string;
    reversedByName: string | null;
  } | null;
}

export interface ReversePaymentRequest extends TillSourceFields {
  reason: string;
  operationKey: string;
}

export interface KeyedRecordCustomerPaymentRequest extends RecordCustomerPaymentRequest {
  operationKey: string;
}

@Injectable({
  providedIn: 'root',
})
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly tillSources = inject(TillSourceService);
  private readonly apiUrl = `${environment.apiBaseUrl}/customers`;
  private readonly paymentsUrl = `${environment.apiBaseUrl}/payments`;

  getCustomers(): Observable<Customer[]> {
    return this.http.get<Customer[]>(this.apiUrl);
  }

  addCustomer(request: AddCustomerRequest): Observable<Customer> {
    return this.http.post<Customer>(this.apiUrl, request);
  }

  editCustomer(customerId: string, request: EditCustomerRequest): Observable<Customer> {
    return this.http.put<Customer>(`${this.apiUrl}/${customerId}`, request);
  }

  getCustomerAccount(customerId: string): Observable<CustomerAccount> {
    return this.http.get<CustomerAccount>(`${this.apiUrl}/${customerId}/account`);
  }

  /** The credit preview for checkout; any member may read it. */
  getCustomerCredit(customerId: string): Observable<CustomerCreditStatus> {
    return this.http.get<CustomerCreditStatus>(`${this.apiUrl}/${customerId}/credit`);
  }

  recordCustomerPayment(
    customerId: string,
    request: KeyedRecordCustomerPaymentRequest,
  ): Observable<CustomerLedgerEntry> {
    return this.tillSources.post<CustomerLedgerEntry>(
      `/customers/${customerId}/payments`,
      'customer-payment',
      customerId,
      request.operationKey,
      request,
    );
  }

  getPaymentReceipt(receiptId: string): Observable<PaymentReceiptDetail> {
    return this.http.get<PaymentReceiptDetail>(`${this.paymentsUrl}/${receiptId}`);
  }

  /** Owner only. The same operation key replays a committed reversal instead of reversing twice. */
  reversePayment(
    receiptId: string,
    request: ReversePaymentRequest,
  ): Observable<PaymentReceiptDetail> {
    return this.tillSources.post<PaymentReceiptDetail>(
      `/payments/${receiptId}/reversal`,
      'payment-reversal',
      receiptId,
      'reversal',
      request,
    );
  }
}

/**
 * The operator-confirmed suggestion: settle open invoices oldest first until the amount runs out.
 * Never sent unless the operator accepts it; whatever is left over stays unallocated credit.
 */
export function suggestOldestFirstAllocations(
  sales: readonly CustomerAccountSale[],
  amount: number,
): PaymentAllocationRequest[] {
  let remaining = Math.round(amount * 100);
  const allocations: PaymentAllocationRequest[] = [];
  const open = [...sales]
    .filter((sale) => (sale.currentOutstandingAmount ?? 0) > 0)
    .sort(
      (a, b) => a.soldAt.localeCompare(b.soldAt) || a.invoiceNumber.localeCompare(b.invoiceNumber),
    );

  for (const sale of open) {
    if (remaining <= 0) break;
    const outstanding = Math.round((sale.currentOutstandingAmount ?? 0) * 100);
    const applied = Math.min(outstanding, remaining);
    allocations.push({ targetId: sale.saleId, amount: applied / 100 });
    remaining -= applied;
  }

  return allocations;
}
