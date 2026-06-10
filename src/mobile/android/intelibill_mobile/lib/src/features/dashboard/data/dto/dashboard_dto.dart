import 'package:freezed_annotation/freezed_annotation.dart';

part 'dashboard_dto.freezed.dart';
part 'dashboard_dto.g.dart';

@freezed
sealed class DashboardDto with _$DashboardDto {
  const factory DashboardDto({
    @JsonKey(name: 'generatedAt') required String generatedAt,
    @JsonKey(name: 'startDate') required String startDate,
    @JsonKey(name: 'endDate') required String endDate,
    @JsonKey(name: 'salesCount') required int salesCount,
    @JsonKey(name: 'salesRevenue') double? salesRevenue,
    @JsonKey(name: 'hasNoSalesActivity') required bool hasNoSalesActivity,
    @JsonKey(name: 'customerCreditDue') required double customerCreditDue,
    @JsonKey(name: 'salesBooked') double? salesBooked,
    @JsonKey(name: 'netSalesBooked') double? netSalesBooked,
    @JsonKey(name: 'wastageCost') double? wastageCost,
    @JsonKey(name: 'cashCollected') double? cashCollected,
    @JsonKey(name: 'profitBeforeTax') double? profitBeforeTax,
    @JsonKey(name: 'profitAfterTax') double? profitAfterTax,
    @JsonKey(name: 'netProfit') double? netProfit,
    @JsonKey(name: 'netProfitChangePercent') double? netProfitChangePercent,
    @JsonKey(name: 'expenseRecorded') double? expenseRecorded,
    @JsonKey(name: 'expenseCorrection') double? expenseCorrection,
    @JsonKey(name: 'netExpense') double? netExpense,
    @JsonKey(name: 'supplierPayables') required double supplierPayables,
    @JsonKey(name: 'creditSalesAmount') double? creditSalesAmount,
    @JsonKey(name: 'creditSalesPercentage') double? creditSalesPercentage,
    @JsonKey(name: 'paymentMix') DashboardPaymentMixDto? paymentMix,
    @JsonKey(name: 'creditShareWarning') bool? creditShareWarning,
    @JsonKey(name: 'runningLowStockCount') required int runningLowStockCount,
    @JsonKey(name: 'lowStockItemCount') required int lowStockItemCount,
    @JsonKey(name: 'criticalStockCount') required int criticalStockCount,
    @JsonKey(name: 'rankedShortageList')
    @Default(<DashboardStockShortageDto>[])
    List<DashboardStockShortageDto> rankedShortageList,
    @JsonKey(name: 'highestDueCustomer')
    DashboardCustomerDueDto? highestDueCustomer,
    @JsonKey(name: 'topFiveDueCustomers')
    List<DashboardCustomerDueDto>? topFiveDueCustomers,
    @JsonKey(name: 'alerts')
    @Default(<DashboardAlertDto>[])
    List<DashboardAlertDto> alerts,
    @JsonKey(name: 'salesTrendSeries')
    List<DashboardSalesTrendPointDto>? salesTrendSeries,
    @JsonKey(name: 'revenueVsExpenses')
    List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses,
    @JsonKey(name: 'profitTrendSeries')
    List<DashboardProfitTrendPointDto>? profitTrendSeries,
    @JsonKey(name: 'paymentMixTrendSeries')
    List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries,
    @JsonKey(name: 'previousPeriodSummary')
    DashboardPreviousPeriodSummaryDto? previousPeriodSummary,
    @JsonKey(name: 'latestSales')
    @Default(<DashboardLatestSaleDto>[])
    List<DashboardLatestSaleDto> latestSales,
    @JsonKey(name: 'stockValue') double? stockValue,
  }) = _DashboardDto;

  factory DashboardDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardDtoFromJson(json);
}

@freezed
sealed class DashboardAlertDto with _$DashboardAlertDto {
  const factory DashboardAlertDto({
    @JsonKey(name: 'alertType') required String alertType,
    @JsonKey(name: 'priority') required int priority,
    @JsonKey(name: 'title') required String title,
    @JsonKey(name: 'message') required String message,
    @JsonKey(name: 'actionLabel') required String actionLabel,
    @JsonKey(name: 'actionRoute') required String actionRoute,
  }) = _DashboardAlertDto;

  factory DashboardAlertDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardAlertDtoFromJson(json);
}

@freezed
sealed class DashboardLatestSaleDto with _$DashboardLatestSaleDto {
  const factory DashboardLatestSaleDto({
    @JsonKey(name: 'saleId') required String saleId,
    @JsonKey(name: 'invoiceNumber') required String invoiceNumber,
    @JsonKey(name: 'customerDisplayName') required String customerDisplayName,
    @JsonKey(name: 'soldAt') required String soldAt,
    @JsonKey(name: 'totalAmount') required double totalAmount,
  }) = _DashboardLatestSaleDto;

  factory DashboardLatestSaleDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardLatestSaleDtoFromJson(json);
}

@freezed
sealed class DashboardSalesTrendPointDto with _$DashboardSalesTrendPointDto {
  const factory DashboardSalesTrendPointDto({
    @JsonKey(name: 'date') required String date,
    @JsonKey(name: 'amount') required double amount,
    @JsonKey(name: 'netAmount') required double netAmount,
  }) = _DashboardSalesTrendPointDto;

  factory DashboardSalesTrendPointDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardSalesTrendPointDtoFromJson(json);
}

@freezed
sealed class DashboardRevenueVsExpensesPointDto
    with _$DashboardRevenueVsExpensesPointDto {
  const factory DashboardRevenueVsExpensesPointDto({
    @JsonKey(name: 'date') required String date,
    @JsonKey(name: 'revenue') required double revenue,
    @JsonKey(name: 'expenses') required double expenses,
  }) = _DashboardRevenueVsExpensesPointDto;

  factory DashboardRevenueVsExpensesPointDto.fromJson(
    Map<String, dynamic> json,
  ) => _$DashboardRevenueVsExpensesPointDtoFromJson(json);
}

@freezed
sealed class DashboardProfitTrendPointDto with _$DashboardProfitTrendPointDto {
  const factory DashboardProfitTrendPointDto({
    @JsonKey(name: 'date') required String date,
    @JsonKey(name: 'profitBeforeTax') required double profitBeforeTax,
    @JsonKey(name: 'profitAfterTax') required double profitAfterTax,
  }) = _DashboardProfitTrendPointDto;

  factory DashboardProfitTrendPointDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardProfitTrendPointDtoFromJson(json);
}

@freezed
sealed class DashboardPaymentMixDto with _$DashboardPaymentMixDto {
  const factory DashboardPaymentMixDto({
    @JsonKey(name: 'cash') required double cash,
    @JsonKey(name: 'upi') required double upi,
    @JsonKey(name: 'card') required double card,
    @JsonKey(name: 'credit') required double credit,
  }) = _DashboardPaymentMixDto;

  factory DashboardPaymentMixDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardPaymentMixDtoFromJson(json);
}

@freezed
sealed class DashboardPaymentMixTrendPointDto
    with _$DashboardPaymentMixTrendPointDto {
  const factory DashboardPaymentMixTrendPointDto({
    @JsonKey(name: 'date') required String date,
    @JsonKey(name: 'cash') required double cash,
    @JsonKey(name: 'upi') required double upi,
    @JsonKey(name: 'card') required double card,
    @JsonKey(name: 'credit') required double credit,
  }) = _DashboardPaymentMixTrendPointDto;

  factory DashboardPaymentMixTrendPointDto.fromJson(
    Map<String, dynamic> json,
  ) => _$DashboardPaymentMixTrendPointDtoFromJson(json);
}

@freezed
sealed class DashboardPreviousPeriodSummaryDto
    with _$DashboardPreviousPeriodSummaryDto {
  const factory DashboardPreviousPeriodSummaryDto({
    @JsonKey(name: 'startDate') required String startDate,
    @JsonKey(name: 'endDate') required String endDate,
    @JsonKey(name: 'salesCount') required int salesCount,
    @JsonKey(name: 'salesBooked') required double salesBooked,
    @JsonKey(name: 'netSalesBooked') required double netSalesBooked,
    @JsonKey(name: 'profitAfterTax') required double profitAfterTax,
    @JsonKey(name: 'netExpense') required double netExpense,
    @JsonKey(name: 'creditSalesPercentage')
    required double creditSalesPercentage,
  }) = _DashboardPreviousPeriodSummaryDto;

  factory DashboardPreviousPeriodSummaryDto.fromJson(
    Map<String, dynamic> json,
  ) => _$DashboardPreviousPeriodSummaryDtoFromJson(json);
}

@freezed
sealed class DashboardStockShortageDto with _$DashboardStockShortageDto {
  const factory DashboardStockShortageDto({
    @JsonKey(name: 'itemName') required String itemName,
    @JsonKey(name: 'quantity') required double quantity,
    @JsonKey(name: 'reorderLevel') required double reorderLevel,
    @JsonKey(name: 'shortage') required double shortage,
  }) = _DashboardStockShortageDto;

  factory DashboardStockShortageDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardStockShortageDtoFromJson(json);
}

@freezed
sealed class DashboardCustomerDueDto with _$DashboardCustomerDueDto {
  const factory DashboardCustomerDueDto({
    @JsonKey(name: 'customerId') required String customerId,
    @JsonKey(name: 'displayName') required String displayName,
    @JsonKey(name: 'outstandingDue') required double outstandingDue,
  }) = _DashboardCustomerDueDto;

  factory DashboardCustomerDueDto.fromJson(Map<String, dynamic> json) =>
      _$DashboardCustomerDueDtoFromJson(json);
}
