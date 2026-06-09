export interface DashboardAlertDto {
  alertType: string;
  priority: number;
  title: string;
  message: string;
  actionLabel: string;
  actionRoute: string;
}

export interface CustomerDueDto {
  customerId: string;
  displayName: string;
  outstandingDue: number;
}

export interface PaymentMixDto {
  cash: number;
  upi: number;
  card: number;
  credit: number;
}

export interface PaymentMixTrendPointDto {
  date: string;
  cash: number;
  upi: number;
  card: number;
  credit: number;
}

export interface ProfitTrendPointDto {
  date: string;
  profitBeforeTax: number;
  profitAfterTax: number;
}

export interface PreviousPeriodSummaryDto {
  startDate: string;
  endDate: string;
  salesCount: number;
  salesBooked: number;
  netSalesBooked: number;
  profitAfterTax: number;
  netExpense: number;
  creditSalesPercentage: number;
}

export interface SalesTrendPointDto {
  date: string;
  amount: number;
  netAmount: number;
}

export interface StockShortageItemDto {
  itemName: string;
  quantity: number;
  reorderLevel: number;
  shortage: number;
}

export interface LatestSaleDto {
  saleId: string;
  invoiceNumber: string;
  customerDisplayName: string;
  soldAt: string;
  totalAmount: number;
}

export interface DashboardDto {
  generatedAt: string;
  startDate: string;
  endDate: string;
  salesCount: number;
  salesRevenue: number | null;
  hasNoSalesActivity: boolean;
  customerCreditDue: number;
  salesBooked: number | null;
  netSalesBooked: number | null;
  wastageCost: number | null;
  cashCollected: number | null;
  profitBeforeTax: number | null;
  profitAfterTax: number | null;
  netProfit: number | null;
  expenseRecorded: number | null;
  expenseCorrection: number | null;
  netExpense: number | null;
  supplierPayables: number;
  creditSalesAmount: number | null;
  creditSalesPercentage: number | null;
  paymentMix: PaymentMixDto | null;
  creditShareWarning: boolean | null;
  runningLowStockCount: number;
  lowStockItemCount: number;
  criticalStockCount: number;
  rankedShortageList: StockShortageItemDto[];
  highestDueCustomer: CustomerDueDto | null;
  topFiveDueCustomers: CustomerDueDto[] | null;
  alerts: DashboardAlertDto[];
  salesTrendSeries: SalesTrendPointDto[] | null;
  profitTrendSeries: ProfitTrendPointDto[] | null;
  paymentMixTrendSeries: PaymentMixTrendPointDto[] | null;
  previousPeriodSummary: PreviousPeriodSummaryDto | null;
  latestSales: LatestSaleDto[];
  stockValue: number | null;
}

export interface DashboardQueryParams {
  from?: string;
  to?: string;
}
