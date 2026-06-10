// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'dashboard_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DashboardDto _$DashboardDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardDto(
  generatedAt: json['generatedAt'] as String,
  startDate: json['startDate'] as String,
  endDate: json['endDate'] as String,
  salesCount: (json['salesCount'] as num).toInt(),
  salesRevenue: (json['salesRevenue'] as num?)?.toDouble(),
  hasNoSalesActivity: json['hasNoSalesActivity'] as bool,
  customerCreditDue: (json['customerCreditDue'] as num).toDouble(),
  salesBooked: (json['salesBooked'] as num?)?.toDouble(),
  netSalesBooked: (json['netSalesBooked'] as num?)?.toDouble(),
  wastageCost: (json['wastageCost'] as num?)?.toDouble(),
  cashCollected: (json['cashCollected'] as num?)?.toDouble(),
  profitBeforeTax: (json['profitBeforeTax'] as num?)?.toDouble(),
  profitAfterTax: (json['profitAfterTax'] as num?)?.toDouble(),
  netProfit: (json['netProfit'] as num?)?.toDouble(),
  netProfitChangePercent: (json['netProfitChangePercent'] as num?)?.toDouble(),
  expenseRecorded: (json['expenseRecorded'] as num?)?.toDouble(),
  expenseCorrection: (json['expenseCorrection'] as num?)?.toDouble(),
  netExpense: (json['netExpense'] as num?)?.toDouble(),
  supplierPayables: (json['supplierPayables'] as num).toDouble(),
  creditSalesAmount: (json['creditSalesAmount'] as num?)?.toDouble(),
  creditSalesPercentage: (json['creditSalesPercentage'] as num?)?.toDouble(),
  paymentMix: json['paymentMix'] == null
      ? null
      : DashboardPaymentMixDto.fromJson(
          json['paymentMix'] as Map<String, dynamic>,
        ),
  creditShareWarning: json['creditShareWarning'] as bool?,
  runningLowStockCount: (json['runningLowStockCount'] as num).toInt(),
  lowStockItemCount: (json['lowStockItemCount'] as num).toInt(),
  criticalStockCount: (json['criticalStockCount'] as num).toInt(),
  rankedShortageList:
      (json['rankedShortageList'] as List<dynamic>?)
          ?.map(
            (e) =>
                DashboardStockShortageDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const <DashboardStockShortageDto>[],
  highestDueCustomer: json['highestDueCustomer'] == null
      ? null
      : DashboardCustomerDueDto.fromJson(
          json['highestDueCustomer'] as Map<String, dynamic>,
        ),
  topFiveDueCustomers: (json['topFiveDueCustomers'] as List<dynamic>?)
      ?.map((e) => DashboardCustomerDueDto.fromJson(e as Map<String, dynamic>))
      .toList(),
  alerts:
      (json['alerts'] as List<dynamic>?)
          ?.map((e) => DashboardAlertDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const <DashboardAlertDto>[],
  salesTrendSeries: (json['salesTrendSeries'] as List<dynamic>?)
      ?.map(
        (e) => DashboardSalesTrendPointDto.fromJson(e as Map<String, dynamic>),
      )
      .toList(),
  revenueVsExpenses: (json['revenueVsExpenses'] as List<dynamic>?)
      ?.map(
        (e) => DashboardRevenueVsExpensesPointDto.fromJson(
          e as Map<String, dynamic>,
        ),
      )
      .toList(),
  profitTrendSeries: (json['profitTrendSeries'] as List<dynamic>?)
      ?.map(
        (e) => DashboardProfitTrendPointDto.fromJson(e as Map<String, dynamic>),
      )
      .toList(),
  paymentMixTrendSeries: (json['paymentMixTrendSeries'] as List<dynamic>?)
      ?.map(
        (e) => DashboardPaymentMixTrendPointDto.fromJson(
          e as Map<String, dynamic>,
        ),
      )
      .toList(),
  previousPeriodSummary: json['previousPeriodSummary'] == null
      ? null
      : DashboardPreviousPeriodSummaryDto.fromJson(
          json['previousPeriodSummary'] as Map<String, dynamic>,
        ),
  latestSales:
      (json['latestSales'] as List<dynamic>?)
          ?.map(
            (e) => DashboardLatestSaleDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const <DashboardLatestSaleDto>[],
  stockValue: (json['stockValue'] as num?)?.toDouble(),
);

Map<String, dynamic> _$DashboardDtoToJson(_DashboardDto instance) =>
    <String, dynamic>{
      'generatedAt': instance.generatedAt,
      'startDate': instance.startDate,
      'endDate': instance.endDate,
      'salesCount': instance.salesCount,
      'salesRevenue': instance.salesRevenue,
      'hasNoSalesActivity': instance.hasNoSalesActivity,
      'customerCreditDue': instance.customerCreditDue,
      'salesBooked': instance.salesBooked,
      'netSalesBooked': instance.netSalesBooked,
      'wastageCost': instance.wastageCost,
      'cashCollected': instance.cashCollected,
      'profitBeforeTax': instance.profitBeforeTax,
      'profitAfterTax': instance.profitAfterTax,
      'netProfit': instance.netProfit,
      'netProfitChangePercent': instance.netProfitChangePercent,
      'expenseRecorded': instance.expenseRecorded,
      'expenseCorrection': instance.expenseCorrection,
      'netExpense': instance.netExpense,
      'supplierPayables': instance.supplierPayables,
      'creditSalesAmount': instance.creditSalesAmount,
      'creditSalesPercentage': instance.creditSalesPercentage,
      'paymentMix': instance.paymentMix,
      'creditShareWarning': instance.creditShareWarning,
      'runningLowStockCount': instance.runningLowStockCount,
      'lowStockItemCount': instance.lowStockItemCount,
      'criticalStockCount': instance.criticalStockCount,
      'rankedShortageList': instance.rankedShortageList,
      'highestDueCustomer': instance.highestDueCustomer,
      'topFiveDueCustomers': instance.topFiveDueCustomers,
      'alerts': instance.alerts,
      'salesTrendSeries': instance.salesTrendSeries,
      'revenueVsExpenses': instance.revenueVsExpenses,
      'profitTrendSeries': instance.profitTrendSeries,
      'paymentMixTrendSeries': instance.paymentMixTrendSeries,
      'previousPeriodSummary': instance.previousPeriodSummary,
      'latestSales': instance.latestSales,
      'stockValue': instance.stockValue,
    };

_DashboardAlertDto _$DashboardAlertDtoFromJson(Map<String, dynamic> json) =>
    _DashboardAlertDto(
      alertType: json['alertType'] as String,
      priority: (json['priority'] as num).toInt(),
      title: json['title'] as String,
      message: json['message'] as String,
      actionLabel: json['actionLabel'] as String,
      actionRoute: json['actionRoute'] as String,
    );

Map<String, dynamic> _$DashboardAlertDtoToJson(_DashboardAlertDto instance) =>
    <String, dynamic>{
      'alertType': instance.alertType,
      'priority': instance.priority,
      'title': instance.title,
      'message': instance.message,
      'actionLabel': instance.actionLabel,
      'actionRoute': instance.actionRoute,
    };

_DashboardLatestSaleDto _$DashboardLatestSaleDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardLatestSaleDto(
  saleId: json['saleId'] as String,
  invoiceNumber: json['invoiceNumber'] as String,
  customerDisplayName: json['customerDisplayName'] as String,
  soldAt: json['soldAt'] as String,
  totalAmount: (json['totalAmount'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardLatestSaleDtoToJson(
  _DashboardLatestSaleDto instance,
) => <String, dynamic>{
  'saleId': instance.saleId,
  'invoiceNumber': instance.invoiceNumber,
  'customerDisplayName': instance.customerDisplayName,
  'soldAt': instance.soldAt,
  'totalAmount': instance.totalAmount,
};

_DashboardSalesTrendPointDto _$DashboardSalesTrendPointDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardSalesTrendPointDto(
  date: json['date'] as String,
  amount: (json['amount'] as num).toDouble(),
  netAmount: (json['netAmount'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardSalesTrendPointDtoToJson(
  _DashboardSalesTrendPointDto instance,
) => <String, dynamic>{
  'date': instance.date,
  'amount': instance.amount,
  'netAmount': instance.netAmount,
};

_DashboardRevenueVsExpensesPointDto
_$DashboardRevenueVsExpensesPointDtoFromJson(Map<String, dynamic> json) =>
    _DashboardRevenueVsExpensesPointDto(
      date: json['date'] as String,
      revenue: (json['revenue'] as num).toDouble(),
      expenses: (json['expenses'] as num).toDouble(),
    );

Map<String, dynamic> _$DashboardRevenueVsExpensesPointDtoToJson(
  _DashboardRevenueVsExpensesPointDto instance,
) => <String, dynamic>{
  'date': instance.date,
  'revenue': instance.revenue,
  'expenses': instance.expenses,
};

_DashboardProfitTrendPointDto _$DashboardProfitTrendPointDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardProfitTrendPointDto(
  date: json['date'] as String,
  profitBeforeTax: (json['profitBeforeTax'] as num).toDouble(),
  profitAfterTax: (json['profitAfterTax'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardProfitTrendPointDtoToJson(
  _DashboardProfitTrendPointDto instance,
) => <String, dynamic>{
  'date': instance.date,
  'profitBeforeTax': instance.profitBeforeTax,
  'profitAfterTax': instance.profitAfterTax,
};

_DashboardPaymentMixDto _$DashboardPaymentMixDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardPaymentMixDto(
  cash: (json['cash'] as num).toDouble(),
  upi: (json['upi'] as num).toDouble(),
  card: (json['card'] as num).toDouble(),
  credit: (json['credit'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardPaymentMixDtoToJson(
  _DashboardPaymentMixDto instance,
) => <String, dynamic>{
  'cash': instance.cash,
  'upi': instance.upi,
  'card': instance.card,
  'credit': instance.credit,
};

_DashboardPaymentMixTrendPointDto _$DashboardPaymentMixTrendPointDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardPaymentMixTrendPointDto(
  date: json['date'] as String,
  cash: (json['cash'] as num).toDouble(),
  upi: (json['upi'] as num).toDouble(),
  card: (json['card'] as num).toDouble(),
  credit: (json['credit'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardPaymentMixTrendPointDtoToJson(
  _DashboardPaymentMixTrendPointDto instance,
) => <String, dynamic>{
  'date': instance.date,
  'cash': instance.cash,
  'upi': instance.upi,
  'card': instance.card,
  'credit': instance.credit,
};

_DashboardPreviousPeriodSummaryDto _$DashboardPreviousPeriodSummaryDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardPreviousPeriodSummaryDto(
  startDate: json['startDate'] as String,
  endDate: json['endDate'] as String,
  salesCount: (json['salesCount'] as num).toInt(),
  salesBooked: (json['salesBooked'] as num).toDouble(),
  netSalesBooked: (json['netSalesBooked'] as num).toDouble(),
  profitAfterTax: (json['profitAfterTax'] as num).toDouble(),
  netExpense: (json['netExpense'] as num).toDouble(),
  creditSalesPercentage: (json['creditSalesPercentage'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardPreviousPeriodSummaryDtoToJson(
  _DashboardPreviousPeriodSummaryDto instance,
) => <String, dynamic>{
  'startDate': instance.startDate,
  'endDate': instance.endDate,
  'salesCount': instance.salesCount,
  'salesBooked': instance.salesBooked,
  'netSalesBooked': instance.netSalesBooked,
  'profitAfterTax': instance.profitAfterTax,
  'netExpense': instance.netExpense,
  'creditSalesPercentage': instance.creditSalesPercentage,
};

_DashboardStockShortageDto _$DashboardStockShortageDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardStockShortageDto(
  itemName: json['itemName'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  reorderLevel: (json['reorderLevel'] as num).toDouble(),
  shortage: (json['shortage'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardStockShortageDtoToJson(
  _DashboardStockShortageDto instance,
) => <String, dynamic>{
  'itemName': instance.itemName,
  'quantity': instance.quantity,
  'reorderLevel': instance.reorderLevel,
  'shortage': instance.shortage,
};

_DashboardCustomerDueDto _$DashboardCustomerDueDtoFromJson(
  Map<String, dynamic> json,
) => _DashboardCustomerDueDto(
  customerId: json['customerId'] as String,
  displayName: json['displayName'] as String,
  outstandingDue: (json['outstandingDue'] as num).toDouble(),
);

Map<String, dynamic> _$DashboardCustomerDueDtoToJson(
  _DashboardCustomerDueDto instance,
) => <String, dynamic>{
  'customerId': instance.customerId,
  'displayName': instance.displayName,
  'outstandingDue': instance.outstandingDue,
};
