export type PaymentMethod = 'Cash' | 'UPI' | 'Card' | 'Credit';
export const PAYMENT_METHOD_VALUES: { value: number; label: PaymentMethod }[] = [
  { value: 1, label: 'Cash' },
  { value: 2, label: 'UPI' },
  { value: 3, label: 'Card' },
  { value: 4, label: 'Credit' },
];

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

export interface SalePreviewLineDto {
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
}

export interface RecordSaleRequest {
  readonly idempotencyKey: string;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly items: readonly RecordSaleItemRequest[];
  readonly saleDiscount: InstantDiscountRequest | null;
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
}

export interface SaleItemDto {
  readonly saleItemId: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly inventoryBatchId: string;
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
}

export interface SaleReturnDto {
  readonly saleReturnId: string;
  readonly returnNumber: string;
  readonly returnedAt: string;
  readonly totalRefundAmount: number;
  readonly dueReductionAmount: number;
  readonly payoutAmount: number;
  readonly isVoided: boolean;
  readonly voidedAt: string | null;
  readonly voidReason: string | null;
  readonly items: readonly SaleReturnItemDto[];
}

export interface SaleReturnItemDto {
  readonly saleReturnItemId: string;
  readonly saleItemId: string;
  readonly quantity: number;
  readonly condition: SaleReturnCondition;
  readonly approvedRefundAmount: number;
  readonly notes: string | null;
}

export interface SaleDto {
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
  readonly items: readonly SaleItemDto[];
  readonly returns: readonly SaleReturnDto[];
  readonly warnings: readonly string[];
}

export type SaleReturnCondition = 1 | 2;

export const SALE_RETURN_CONDITIONS: { value: SaleReturnCondition; label: string }[] = [
  { value: 1, label: 'Restockable' },
  { value: 2, label: 'Wastage' },
];

export interface PreviewSaleReturnItemRequest {
  readonly saleItemId: string;
  readonly quantity: number;
  readonly condition: SaleReturnCondition;
  readonly approvedRefundAmount: number | null;
  readonly notes: string | null;
}

export interface PreviewSaleReturnRequest {
  readonly dueReductionOverrideAmount: number | null;
  readonly dueOverrideReason: string | null;
  readonly items: readonly PreviewSaleReturnItemRequest[];
}

export interface RecordSaleReturnRequest extends PreviewSaleReturnRequest {
  readonly payoutMethod: number | null;
  readonly notes: string | null;
}

export interface VoidSaleReturnRequest {
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
  readonly itemId: string;
  readonly inventoryBatchId: string;
  readonly requestedQuantity: number;
  readonly returnedQuantity: number;
  readonly returnableQuantity: number;
  readonly condition: SaleReturnCondition;
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
}

export type ProfitLossReportRowType = 'Sale' | 'SaleReturn' | 'InventoryAdjustment';

export interface ProfitLossReportItemDto {
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
  readonly rowType: ProfitLossReportRowType;
  readonly inventoryAdjustmentId: string | null;
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
}

export interface OfflineSaleSyncRequest {
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
  readonly items: readonly OfflineSalesSyncLineRequest[];
}

export interface OfflineSalesSyncRequest {
  readonly deviceId: string;
  readonly sales: readonly OfflineSaleSyncRequest[];
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
}

export interface OfflineSalesSyncResponseDto {
  readonly results: readonly OfflineSaleSyncResultDto[];
}
