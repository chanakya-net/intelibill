import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';

export type PaymentMethod = 'Cash' | 'UPI' | 'Card' | 'Credit';
export const PAYMENT_METHOD_VALUES: { value: number; label: PaymentMethod }[] = [
  { value: 1, label: 'Cash' },
  { value: 2, label: 'UPI' },
  { value: 3, label: 'Card' },
  { value: 4, label: 'Credit' },
];

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
}

export interface RecordSaleRequest {
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly items: readonly RecordSaleItemRequest[];
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

@Injectable({ providedIn: 'root' })
export class SaleService {
  private readonly http = inject(HttpClient);

  recordSale(request: RecordSaleRequest): Observable<SaleDto> {
    return this.http.post<SaleDto>(SALE_ENDPOINTS.record, request);
  }

  getSales(): Observable<readonly SaleListItemDto[]> {
    return this.http.get<readonly SaleListItemDto[]>(SALE_ENDPOINTS.list);
  }

  getSaleById(saleId: string): Observable<SaleDto> {
    return this.http.get<SaleDto>(SALE_ENDPOINTS.detail(saleId));
  }

  previewSaleReturn(saleId: string, request: PreviewSaleReturnRequest): Observable<SaleReturnPreviewDto> {
    return this.http.post<SaleReturnPreviewDto>(`${SALE_ENDPOINTS.detail(saleId)}/returns/preview`, request);
  }

  recordSaleReturn(saleId: string, request: RecordSaleReturnRequest): Observable<SaleDto> {
    return this.http.post<SaleDto>(`${SALE_ENDPOINTS.detail(saleId)}/returns`, request);
  }

  voidSaleReturn(saleReturnId: string, request: VoidSaleReturnRequest): Observable<void> {
    return this.http.post<void>(`${SALE_ENDPOINTS.record}/returns/${saleReturnId}/void`, request);
  }

  getProfitLossReport(): Observable<readonly ProfitLossReportItemDto[]> {
    return this.http.get<readonly ProfitLossReportItemDto[]>(SALE_ENDPOINTS.profitLoss);
  }
}
