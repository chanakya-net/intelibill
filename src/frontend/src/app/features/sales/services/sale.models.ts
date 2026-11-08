import type { TillSourceFields } from '../../tills/services/till-source.service';
import type { SaleCancellationDto } from './sale-cancellation.models';
// ─── Sellable DTOs (GET /api/sales/sellables) ───────────────────────────────

export interface SellableGoodsDto {
  readonly kind: 'Goods';
  readonly inventoryBatchId: string;
  readonly barcode: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly expiryDate: string | null;
  readonly hsnCode?: string | null;
  /** The item's unit label. */
  readonly uom?: string | null;
  /** Decimal places a quantity of the item may carry; absent or null means whole units only. */
  readonly quantityScale?: number | null;
  /** Unit code its documents print, known for offline snapshot rows only; absent means NOS. */
  readonly documentUnit?: string;
}

export interface SellableServiceDto {
  readonly kind: 'Service';
  readonly serviceId: string;
  readonly code: string;
  readonly name: string;
  readonly description: string | null;
  readonly price: number;
  readonly hsnCode: string | null;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
}

export type SellableDto = SellableGoodsDto | SellableServiceDto;

// ─── Service cart line ───────────────────────────────────────────────────────

export interface ServiceCartLine {
  readonly kind: 'service';
  readonly clientLineKey: string;
  readonly serviceId: string;
  readonly serviceName: string;
  readonly serviceCode: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly hsnCode: string | null;
}

// ─── Payment ─────────────────────────────────────────────────────────────────

export enum ReturnPayoutDestination {
  CreditNote = 1,
  Refund = 2,
}

export interface ReturnPayoutDestinationOption {
  readonly value: number;
  readonly labelKey: string;
}

export const RETURN_PAYOUT_DESTINATION_OPTIONS: ReturnPayoutDestinationOption[] = [
  { value: 1, labelKey: 'sales.returns.preview.payoutDestinations.cash' },
  { value: 2, labelKey: 'sales.returns.preview.payoutDestinations.upi' },
  { value: 3, labelKey: 'sales.returns.preview.payoutDestinations.card' },
  { value: 4, labelKey: 'sales.returns.preview.payoutDestinations.creditNote' },
];

export function mapPayoutDestinationSelectionToReturnPayoutDestination(
  destination: number | null,
): ReturnPayoutDestination | null {
  if (destination === null) return null;
  return destination === 4 ? ReturnPayoutDestination.CreditNote : ReturnPayoutDestination.Refund;
}

export function mapPaymentMethodToPayoutDestination(
  method: number | null,
): ReturnPayoutDestination | null {
  if (method === null) return null;
  return ReturnPayoutDestination.Refund;
}

export type PaymentMethod = 'Cash' | 'UPI' | 'Card' | 'Credit';
export const PAYMENT_METHOD_VALUES: { value: number; label: PaymentMethod }[] = [
  { value: 1, label: 'Cash' },
  { value: 2, label: 'UPI' },
  { value: 3, label: 'Card' },
  { value: 4, label: 'Credit' },
];

/** The API sends the method by name ("Cash"); older cached sales and offline records carry its number. */
export function getPaymentMethodLabel(method: number | string): string {
  const entry =
    typeof method === 'string'
      ? PAYMENT_METHOD_VALUES.find(
          (candidate) => candidate.label.toLowerCase() === method.toLowerCase(),
        )
      : PAYMENT_METHOD_VALUES.find((candidate) => candidate.value === method);
  return entry?.label ?? 'Unknown';
}

export function getPaymentMethodSeverity(
  method: number,
): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (method) {
    case 1:
      return 'success';
    case 2:
      return 'info';
    case 3:
      return 'warn';
    case 4:
      return 'danger';
    default:
      return 'secondary';
  }
}

export type InstantDiscountType = 0 | 1 | 2; // 0=None, 1=Percentage, 2=Flat

export interface InstantDiscountRequest {
  readonly type: InstantDiscountType;
  readonly value: number;
}

export const NO_DISCOUNT: InstantDiscountRequest = { type: 0, value: 0 };

export interface PreviewSaleItemRequest {
  readonly inventoryBatchId: string;
  readonly barcode: string;
  readonly batchNumber: string;
  readonly itemName: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
  readonly itemDiscount: InstantDiscountRequest;
  readonly clientLineKey: string;
  readonly hsnCode: string | null;
  readonly lineType?: 'Goods' | 'Service';
  readonly serviceId?: string | null;
}

export interface PreviewSaleRequest {
  readonly saleDiscount: InstantDiscountRequest;
  readonly items: readonly PreviewSaleItemRequest[];
}

export interface SalePreviewConfiguredSaleRuleDto {
  readonly ruleId: string;
  readonly ruleType: string;
  readonly percentage: number;
  readonly thresholdAmount: number | null;
}

export interface EconomicLine {
  readonly netRevenue: number;
  readonly economicCost: number;
  readonly grossProfit: number;
  readonly grossMarginPercent: number | null;
  readonly markupPercent: number | null;
}

export interface ProfitLossRowEconomicsDto {
  readonly version: number;
  readonly netSalesExcludingTax: number;
  readonly cogs: number;
  readonly stockLoss: number;
  readonly returnedGoodsWastageIncludedInCogs: number;
  readonly grossTradingProfit: number;
  readonly grossMarginPercent: number | null;
  readonly markupPercent: number | null;
  readonly hasUnmodeledServiceCost: boolean;
}

export interface ProfitLossEconomicsDto extends Omit<
  ProfitLossRowEconomicsDto,
  'hasUnmodeledServiceCost'
> {
  readonly costBasis: string;
  readonly operatingExpenses: number | null;
  readonly operatingProfit: number | null;
  readonly unclassifiedExpenseCount: number;
  readonly unclassifiedExpenseAmount: number;
  readonly operatingProfitStatus:
    | 'Complete'
    | 'FilteredActivity'
    | 'UnclassifiedExpenses'
    | 'AccountingReview'
    | 'ServiceCostsUnmodeled';
  readonly limitations: readonly string[];
}

export interface SalePreviewLineDto {
  readonly economics?: EconomicLine | null;
  readonly itemId: string;
  readonly barcode: string;
  readonly itemName: string;
  readonly inventoryBatchId: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
  readonly preTaxAmountBeforeDiscount: number;
  readonly itemDiscountAmount: number;
  readonly saleDiscountAmount: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
  readonly lineTotalAmount: number;
  readonly maxAllowedItemDiscountFlat: number;
  readonly maxAllowedItemDiscountPercent: number;
  readonly configuredBatchRuleId: string | null;
  readonly configuredBatchRulePercentage: number | null;
  readonly hasClientPriceMismatch: boolean;
  readonly clientLineKey: string | null;
  readonly lineType?: 'Goods' | 'Service';
}

export interface SalePreviewInfoDto {
  readonly code: string;
  readonly message: string;
}

export interface SalePreviewWarningDto {
  readonly code: string;
  readonly message: string;
  readonly severity: string;
  readonly inventoryBatchId: string;
  readonly clientLineKey: string | null;
}

export interface SalePreviewDto {
  readonly totalAmount: number;
  readonly totalTaxableAmount: number;
  readonly totalTaxAmount: number;
  readonly totalDiscountAmount: number;
  readonly saleLevelEligibleSubtotal: number;
  readonly configuredSaleRule: SalePreviewConfiguredSaleRuleDto | null;
  readonly lines: readonly SalePreviewLineDto[];
  readonly infos: readonly SalePreviewInfoDto[];
  readonly warnings: readonly SalePreviewWarningDto[];
}

export interface RecordSaleItemRequest {
  readonly barcode: string;
  readonly batchNumber: string;
  readonly itemName: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
  readonly inventoryBatchId: string;
  readonly clientLineKey: string | null;
  readonly itemDiscount: InstantDiscountRequest | null;
  readonly hsnCode: string | null;
  readonly lineType?: 'Goods' | 'Service';
  readonly serviceId?: string | null;
}

export interface CreditNoteRedemptionRequest {
  readonly creditNoteId: string;
  readonly code: string;
  readonly amount: number;
}

export interface AppliedCreditNote {
  readonly creditNoteId: string;
  readonly code: string;
  readonly availableBalance: number;
  readonly expiresAt: string | null;
  readonly status: CreditNoteStatus;
  readonly amount: number;
}

export interface RecordSaleRequest extends TillSourceFields {
  readonly idempotencyKey: string;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly items: readonly RecordSaleItemRequest[];
  readonly saleDiscount: InstantDiscountRequest | null;
  readonly creditNoteAppliedAmount?: number;
  readonly creditNoteRedemptions?: readonly CreditNoteRedemptionRequest[];
  readonly creditNoteCustomerMismatchConfirmed?: boolean;
  /** GST state code of the place of supply; omitted or null means the seller's state (a counter sale). */
  readonly placeOfSupplyStateCode?: string | null;
  /** An owner's or manager's approval to go over the customer's enforced credit limit. */
  readonly creditOverride?: SaleCreditOverride | null;
}

/** Approval to exceed an enforced credit limit; refused once the balance grows past `observedExposure`. */
export interface SaleCreditOverride {
  readonly reason: string;
  readonly observedExposure: number;
}

export interface ReserveInvoiceLeaseRequest {
  readonly deviceId: string;
  readonly blockSize?: number | null;
}

export interface InvoiceLeaseDto {
  readonly leaseId: string;
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly prefix: string;
  readonly numberPadding: number;
  readonly rangeStart: number;
  readonly rangeEnd: number;
  readonly nextNumber: number;
  readonly remainingCount: number;
  readonly reservedAt: string;
  readonly expiresAt: string;
  /** The seller identity the lease froze; the device issues the range's documents under it. Absent from older servers. */
  readonly sellerProfile?: InvoiceSellerProfile | null;
}

export interface SaleItemDto {
  readonly saleItemId: string;
  readonly lineType: 'Goods' | 'Service';
  readonly itemId: string | null;
  readonly serviceId: string | null;
  readonly itemName: string;
  readonly lineCode: string;
  readonly inventoryBatchId: string | null;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly originalSalesPrice: number;
  readonly finalSalesPrice: number;
  readonly preTaxAmountBeforeDiscount: number;
  readonly itemDiscountAmount: number;
  readonly saleDiscountAmount: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
  readonly totalAmount: number;
  readonly savingsAmount: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
  readonly hasPriceMismatch: boolean;
  readonly returnedQuantity: number;
  readonly returnableQuantity: number;
  readonly returnStatus: string;
  readonly hsnCode: string | null;
  /** The item's unit label; null for a service line. */
  readonly uom: string | null;
  /** Decimal places a returned quantity of this line may carry (covers the sold quantity of a legacy line). */
  readonly quantityScale: number;
}

export interface SaleReturnDto {
  readonly refundTender?: import('../../tills/services/till.models').TenderLine | null;
  readonly refundConfirmed?: boolean | null;
  readonly voidSettlementEvidence?:
    | import('../../tills/services/till.models').TillSettlementEvidence
    | null;
  readonly saleReturnId: string;
  readonly returnNumber: string;
  readonly returnedAt: string;
  readonly processedBy?: string;
  readonly notes?: string | null;
  readonly totalRefundAmount: number;
  readonly dueReductionAmount: number;
  readonly payoutAmount: number;
  readonly payoutDestination?: ReturnPayoutDestination | null;
  readonly totalTaxableAmount?: number;
  readonly totalTaxAmount?: number;
  readonly creditNote?: SaleReturnCreditNoteSummaryDto | null;
  readonly isVoided: boolean;
  readonly voidedAt: string | null;
  readonly voidReason: string | null;
  readonly items: readonly SaleReturnItemDto[];
}

export interface SaleReturnItemDto {
  readonly saleReturnItemId: string;
  readonly saleItemId: string;
  readonly quantity: number;
  readonly condition: SaleReturnCondition | null;
  readonly approvedRefundAmount: number;
  readonly taxableAmount?: number;
  readonly taxAmount?: number;
  readonly notes?: string | null;
}

export interface SaleReturnCreditNoteSummaryDto {
  readonly creditNoteId: string;
  readonly code: string;
  readonly originalAmount: number;
  readonly availableBalance: number;
  readonly expiresAt: string | null;
  readonly reason: string;
}

/** A credit note redeemed against this sale (not one its returns issued). */
export interface SaleCreditNoteRedemptionDto {
  readonly creditNoteId: string;
  readonly code: string;
  readonly appliedAmount: number;
}

// ─── Issued GST document (snapshot version 2) ─────────────────────────────────
// Frozen at posting; prints render only from it. Enums travel as their names.

export type InvoiceDocumentType =
  | 'TaxInvoice'
  | 'InvoiceCumBillOfSupply'
  | 'BillOfSupply'
  | 'Receipt';
export type InvoiceSellerTaxRegime = 'Regular' | 'Composition' | 'Unregistered';
export type TaxComponentKind = 'Cgst' | 'Sgst' | 'Utgst' | 'Igst' | 'Cess';
export type InvoiceDeclaration =
  | 'CompositionTaxablePerson'
  | 'EInvoiceExemption'
  | 'NotRegisteredUnderGst';

export interface TaxComponent {
  readonly kind: TaxComponentKind;
  readonly ratePercent: number;
  readonly taxableBase: number;
  readonly amount: number;
}

export interface InvoiceDocumentSeller {
  readonly legalName: string;
  readonly tradeName: string;
  readonly address: string;
  readonly stateCode: string;
  readonly stateName: string;
  readonly gstin: string | null;
  readonly taxRegime: InvoiceSellerTaxRegime;
}

export type InvoiceEInvoiceStatus = 'NotAttested' | 'BelowThreshold' | 'Exempt' | 'Required';

/** The seller identity and capability a shop had when it reserved an invoice lease. */
export interface InvoiceSellerProfile {
  readonly seller: InvoiceDocumentSeller;
  readonly eInvoiceStatus: InvoiceEInvoiceStatus;
  readonly seriesCode: string;
}

export interface InvoiceDocumentBuyer {
  readonly name: string | null;
  readonly phone: string | null;
  readonly address: string | null;
  readonly gstin: string | null;
  readonly stateCode: string | null;
  readonly stateName: string | null;
}

export interface InvoiceDocumentLine {
  readonly lineNumber: number;
  readonly description: string;
  readonly hsnOrSac: string | null;
  readonly uom: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly discountAmount: number;
  readonly taxableValue: number;
  readonly taxRatePercent: number;
  readonly isExempt: boolean;
  readonly taxComponents: readonly TaxComponent[];
  readonly lineTotal: number;
}

export interface InvoiceDocumentTotals {
  readonly taxableValue: number;
  readonly exemptValue: number;
  readonly components: readonly TaxComponent[];
  readonly totalTax: number;
  readonly roundingAdjustment: number;
  readonly invoiceTotal: number;
}

export interface InvoiceDocument {
  readonly version: 2;
  readonly documentType: InvoiceDocumentType;
  readonly issuedAt: string;
  /** India business date, YYYY-MM-DD. */
  readonly businessDate: string;
  readonly fiscalYearStart: number;
  readonly seriesCode: string;
  readonly number: string;
  readonly seller: InvoiceDocumentSeller;
  readonly buyer: InvoiceDocumentBuyer;
  readonly placeOfSupplyStateCode: string;
  readonly placeOfSupplyStateName: string;
  readonly isInterstate: boolean;
  readonly reverseCharge: boolean;
  readonly lines: readonly InvoiceDocumentLine[];
  readonly totals: InvoiceDocumentTotals;
  readonly declarations: readonly InvoiceDeclaration[];
  readonly signatureMode: 'AuthorisedSignatory';
  /** The component split rule, e.g. 'component-split/1'. */
  readonly taxCalculation: string;
}

export interface SaleDto {
  readonly status?: 'Posted' | 'Cancelled';
  readonly version?: number;
  readonly cancellation?: SaleCancellationDto | null;
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: number;
  readonly soldAt: string;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly totalBeforeDiscount: number;
  readonly totalDiscountAmount: number;
  readonly totalAmount: number;
  readonly totalTaxAmount: number;
  readonly creditNoteAppliedAmount: number;
  readonly creditNoteRedemptions: readonly SaleCreditNoteRedemptionDto[];
  readonly items: readonly SaleItemDto[];
  readonly returns: readonly SaleReturnDto[];
  readonly warnings: readonly string[];
  /** The issued GST document; null for a legacy sale posted before documents existed. */
  readonly document: InvoiceDocument | null;
  readonly isLegacyDocument: boolean;
}

export type SaleHistoryStatus =
  | 'paid'
  | 'partiallyPaid'
  | 'refunded'
  | 'unknown'
  | 'returned'
  | 'cancelled';

export interface SalesHistorySummaryDto {
  readonly cancellationAmount?: number;
  readonly cancellationCount?: number;
  readonly periodSales: number;
  readonly invoiceCount: number;
  readonly refundAmount: number;
}

export interface SalesHistoryResultDto {
  readonly items: readonly SaleListItemDto[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly summary: SalesHistorySummaryDto;
}

export interface SalesHistoryQueryParams {
  readonly from?: string;
  readonly to?: string;
  readonly search?: string;
  readonly status?: SaleHistoryStatus;
  readonly page?: number;
  readonly pageSize?: number;
}

export type SaleReturnCondition = 1 | 2;

export const SALE_RETURN_CONDITIONS: { value: SaleReturnCondition; labelKey: string }[] = [
  { value: 1, labelKey: 'sales.returns.preview.conditions.restockable' },
  { value: 2, labelKey: 'sales.returns.preview.conditions.wastage' },
];

export interface PreviewSaleReturnItemRequest {
  readonly saleItemId: string;
  readonly quantity: number;
  readonly lineType?: 'Goods' | 'Service';
  readonly condition: SaleReturnCondition | null;
  readonly approvedRefundAmount: number | null;
  readonly notes: string | null;
}

export interface PreviewSaleReturnRequest {
  readonly dueReductionOverrideAmount: number | null;
  readonly dueOverrideReason: string | null;
  readonly items: readonly PreviewSaleReturnItemRequest[];
}

export interface RecordSaleReturnRequest extends PreviewSaleReturnRequest, TillSourceFields {
  readonly payoutDestination: ReturnPayoutDestination | null;
  readonly notes: string | null;
}

export interface VoidSaleReturnRequest extends TillSourceFields {
  readonly reason: string;
}

export interface VoidCreditNoteRequest {
  readonly reason: string;
}

export interface SaleReturnPreviewLineFinancialDto {
  readonly originalCostPrice: number;
  readonly originalSalesPrice: number;
  readonly originalTaxRatePercent: number;
  readonly originalIsPriceIncludingTax: boolean;
  readonly maxRefundAmount: number;
  readonly approvedRefundAmount: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
}

export interface SaleReturnPreviewLineDto {
  readonly saleItemId: string;
  readonly itemId: string | null;
  readonly inventoryBatchId: string | null;
  readonly requestedQuantity: number;
  readonly returnedQuantity: number;
  readonly returnableQuantity: number;
  readonly condition: SaleReturnCondition | null;
  readonly willRestock: boolean;
  readonly financial: SaleReturnPreviewLineFinancialDto | null;
}

export interface SaleReturnPreviewFinancialDto {
  readonly totalRefundAmount: number;
  readonly dueReductionAmount: number;
  readonly payoutAmount: number;
  readonly totalTaxableAmount: number;
  readonly totalTaxAmount: number;
  readonly customerBalanceBefore: number | null;
  readonly customerBalanceAfter: number | null;
}

export interface SaleReturnPreviewWarningDto {
  readonly code: string;
  readonly message: string;
  readonly severity: string;
}

export interface SaleReturnPreviewDto {
  readonly saleId: string;
  readonly hasFinancialAccess: boolean;
  readonly lines: readonly SaleReturnPreviewLineDto[];
  readonly financial: SaleReturnPreviewFinancialDto | null;
  readonly warnings: readonly SaleReturnPreviewWarningDto[];
}

export interface SaleListItemDto {
  readonly cancellationReference?: string | null;
  readonly cancelledAt?: string | null;
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly customerId: string | null;
  readonly paymentMethod: number;
  readonly soldAt: string;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly totalBeforeDiscount: number;
  readonly totalDiscountAmount: number;
  readonly totalAmount: number;
  readonly totalTaxAmount: number;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly itemCount: number;
  readonly returnNumbers: readonly string[];
  readonly status: SaleHistoryStatus;
  readonly refundAmount: number;
  readonly dueReductionAmount: number;
  readonly creditNoteAppliedAmount: number;
}

export type ProfitLossReportRowType =
  | 'Sale'
  | 'SaleReturn'
  | 'InventoryAdjustment'
  | 'saleCancellation';
export type ProfitLossReportTypeFilter =
  | 'all'
  | 'sale'
  | 'saleReturn'
  | 'inventoryAdjustment'
  | 'saleCancellation';

export interface ProfitLossReportItemDto {
  readonly economics?: ProfitLossRowEconomicsDto | null;
  readonly isReversal?: boolean;
  readonly needsAccountingReview?: boolean;
  readonly saleId: string | null;
  readonly referenceNumber: string;
  readonly occurredAt: string;
  readonly partyName: string | null;
  readonly totalCost: number;
  readonly wastageCost: number;
  readonly revenueBeforeTax: number;
  readonly revenueAfterTax: number;
  readonly profitBeforeTax: number;
  readonly profitAfterTax: number;
  readonly marginPercent: number | null;
  readonly rowType: ProfitLossReportRowType;
  readonly inventoryAdjustmentId: string | null;
}

export interface ProfitLossReportQueryParams {
  readonly from?: string;
  readonly to?: string;
  readonly type?: ProfitLossReportTypeFilter;
  readonly search?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface ProfitLossSummaryDto {
  readonly cancellationAmount?: number;
  readonly cancellationCount?: number;
  readonly economics?: ProfitLossEconomicsDto | null;
  readonly netProfitAfterTax: number;
  readonly revenueIncludingTax: number;
  readonly totalCost: number;
  readonly averageMarginPercent: number | null;
  readonly invoiceCount: number;
  readonly returnCount: number;
  readonly adjustmentCount: number;
}

export interface ProfitLossAppliedFiltersDto {
  readonly from: string;
  readonly to: string;
  readonly type: ProfitLossReportTypeFilter;
  readonly search: string | null;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export interface ProfitLossReportResultDto {
  readonly items: readonly ProfitLossReportItemDto[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly summary: ProfitLossSummaryDto;
  readonly appliedFilters: ProfitLossAppliedFiltersDto;
}

export interface OfflineSalesSyncLineRequest {
  readonly barcode: string;
  readonly batchNumber: string;
  readonly itemName: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
  readonly inventoryBatchId: string;
  readonly preTaxAmountBeforeDiscount: number;
  readonly itemDiscountAmount: number;
  readonly saleDiscountAmount: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
  readonly totalAmount: number;
  readonly configuredBatchRuleId: string | null;
  readonly configuredBatchRulePercentage: number | null;
  readonly itemDiscountOverrideType: number;
  readonly itemDiscountOverrideValue: number;
  readonly hsnCode: string | null;
  readonly lineType?: string;
  readonly serviceId?: string | null;
}

export interface OfflineSaleSyncRequest extends TillSourceFields {
  readonly tillBusinessDate?: string;
  readonly tillCashierId?: string;
  readonly clientSaleId: string;
  readonly invoiceNumber: string;
  readonly soldAt: string;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly subtotalBeforeDiscount: number;
  readonly totalBeforeDiscount: number;
  readonly totalDiscountAmount: number;
  readonly totalTaxAmount: number;
  readonly totalAmount: number;
  readonly saleDiscountOverrideType: number;
  readonly saleDiscountOverrideValue: number;
  readonly configuredSaleRuleId: string | null;
  readonly configuredSaleRuleType: string | null;
  readonly configuredSaleRulePercentage: number | null;
  readonly configuredSaleRuleThresholdAmount: number | null;
  /** Omitted for payloads frozen before calculation versions existed, which the server replays as legacy. */
  readonly calculationVersion?: number;
  /** Omitted for legacy payloads; the server then attributes the sale to the submitter. */
  readonly issuedByUserId?: string;
  readonly items: readonly OfflineSalesSyncLineRequest[];
}

export interface OfflineSalesSyncRequest {
  readonly deviceId: string;
  readonly sales: readonly OfflineSaleSyncRequest[];
}

// ─── Credit note verify ───────────────────────────────────────────────────────

export type CreditNoteStatus = 'Active' | 'Voided' | 'FullyRedeemed' | 'Expired';

export type CreditNoteListStatusFilter = 'all' | CreditNoteStatus;

export interface CreditNoteVerifyResponseDto {
  readonly creditNoteId: string;
  readonly code: string;
  readonly availableBalance: number;
  readonly expiresAt: string | null;
  readonly status: CreditNoteStatus;
  readonly originalAmount?: number;
  readonly isVoided?: boolean;
  readonly saleReturnId?: string;
  readonly reason?: string;
  readonly voidReason?: string | null;
  readonly returnNumber?: string;
  readonly invoiceNumber?: string;
  readonly customerName?: string | null;
}

export type CreditNoteDetailDto = CreditNoteVerifyResponseDto;

export interface CreditNoteListItemDto {
  readonly creditNoteId: string;
  readonly code: string;
  readonly status: CreditNoteStatus;
  readonly originalAmount: number;
  readonly availableBalance: number;
  readonly expiresAt: string | null;
  readonly issuedAt: string;
  readonly saleReturnId: string;
  readonly returnNumber: string;
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly customerName: string | null;
}

export interface CreditNotesQueryParams {
  readonly search?: string;
  readonly status?: CreditNoteStatus;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface CreditNotesResultDto {
  readonly items: readonly CreditNoteListItemDto[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export interface CreditNotePrintDto {
  readonly creditNoteId: string;
  readonly code: string;
  readonly status: string;
  readonly isUsable: boolean;
  readonly originalAmount: number;
  readonly availableBalance: number;
  readonly issuedAt: string;
  readonly expiresAt: string | null;
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly saleReturnId: string;
  readonly returnNumber: string;
  readonly customerDisplayName: string;
  readonly reason: string;
  readonly voidReason: string | null;
}

export interface OfflineSaleSyncErrorDto {
  readonly code: string;
  readonly message: string;
}

export interface OfflineSaleSyncResultDto {
  readonly clientSaleId: string;
  readonly status: string;
  readonly saleId: string | null;
  readonly invoiceNumber: string | null;
  readonly errors: readonly OfflineSaleSyncErrorDto[];
  readonly warnings: readonly string[];
  readonly reviewCaseId?: string | null;
  readonly mismatches?: readonly OfflineArithmeticMismatchDto[];
}

export interface OfflineArithmeticMismatchDto {
  readonly path: string;
  readonly supplied: number | null;
  readonly expected: number | null;
  readonly code: string;
}

export interface OfflineSalesSyncResponseDto {
  readonly results: readonly OfflineSaleSyncResultDto[];
}
