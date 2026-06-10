// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'dashboard_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DashboardDto {

@JsonKey(name: 'generatedAt') String get generatedAt;@JsonKey(name: 'startDate') String get startDate;@JsonKey(name: 'endDate') String get endDate;@JsonKey(name: 'salesCount') int get salesCount;@JsonKey(name: 'salesRevenue') double? get salesRevenue;@JsonKey(name: 'hasNoSalesActivity') bool get hasNoSalesActivity;@JsonKey(name: 'customerCreditDue') double get customerCreditDue;@JsonKey(name: 'salesBooked') double? get salesBooked;@JsonKey(name: 'netSalesBooked') double? get netSalesBooked;@JsonKey(name: 'wastageCost') double? get wastageCost;@JsonKey(name: 'cashCollected') double? get cashCollected;@JsonKey(name: 'profitBeforeTax') double? get profitBeforeTax;@JsonKey(name: 'profitAfterTax') double? get profitAfterTax;@JsonKey(name: 'netProfit') double? get netProfit;@JsonKey(name: 'netProfitChangePercent') double? get netProfitChangePercent;@JsonKey(name: 'expenseRecorded') double? get expenseRecorded;@JsonKey(name: 'expenseCorrection') double? get expenseCorrection;@JsonKey(name: 'netExpense') double? get netExpense;@JsonKey(name: 'supplierPayables') double get supplierPayables;@JsonKey(name: 'creditSalesAmount') double? get creditSalesAmount;@JsonKey(name: 'creditSalesPercentage') double? get creditSalesPercentage;@JsonKey(name: 'paymentMix') DashboardPaymentMixDto? get paymentMix;@JsonKey(name: 'creditShareWarning') bool? get creditShareWarning;@JsonKey(name: 'runningLowStockCount') int get runningLowStockCount;@JsonKey(name: 'lowStockItemCount') int get lowStockItemCount;@JsonKey(name: 'criticalStockCount') int get criticalStockCount;@JsonKey(name: 'rankedShortageList') List<DashboardStockShortageDto> get rankedShortageList;@JsonKey(name: 'highestDueCustomer') DashboardCustomerDueDto? get highestDueCustomer;@JsonKey(name: 'topFiveDueCustomers') List<DashboardCustomerDueDto>? get topFiveDueCustomers;@JsonKey(name: 'alerts') List<DashboardAlertDto> get alerts;@JsonKey(name: 'salesTrendSeries') List<DashboardSalesTrendPointDto>? get salesTrendSeries;@JsonKey(name: 'revenueVsExpenses') List<DashboardRevenueVsExpensesPointDto>? get revenueVsExpenses;@JsonKey(name: 'profitTrendSeries') List<DashboardProfitTrendPointDto>? get profitTrendSeries;@JsonKey(name: 'paymentMixTrendSeries') List<DashboardPaymentMixTrendPointDto>? get paymentMixTrendSeries;@JsonKey(name: 'previousPeriodSummary') DashboardPreviousPeriodSummaryDto? get previousPeriodSummary;@JsonKey(name: 'latestSales') List<DashboardLatestSaleDto> get latestSales;@JsonKey(name: 'stockValue') double? get stockValue;
/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardDtoCopyWith<DashboardDto> get copyWith => _$DashboardDtoCopyWithImpl<DashboardDto>(this as DashboardDto, _$identity);

  /// Serializes this DashboardDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardDto&&(identical(other.generatedAt, generatedAt) || other.generatedAt == generatedAt)&&(identical(other.startDate, startDate) || other.startDate == startDate)&&(identical(other.endDate, endDate) || other.endDate == endDate)&&(identical(other.salesCount, salesCount) || other.salesCount == salesCount)&&(identical(other.salesRevenue, salesRevenue) || other.salesRevenue == salesRevenue)&&(identical(other.hasNoSalesActivity, hasNoSalesActivity) || other.hasNoSalesActivity == hasNoSalesActivity)&&(identical(other.customerCreditDue, customerCreditDue) || other.customerCreditDue == customerCreditDue)&&(identical(other.salesBooked, salesBooked) || other.salesBooked == salesBooked)&&(identical(other.netSalesBooked, netSalesBooked) || other.netSalesBooked == netSalesBooked)&&(identical(other.wastageCost, wastageCost) || other.wastageCost == wastageCost)&&(identical(other.cashCollected, cashCollected) || other.cashCollected == cashCollected)&&(identical(other.profitBeforeTax, profitBeforeTax) || other.profitBeforeTax == profitBeforeTax)&&(identical(other.profitAfterTax, profitAfterTax) || other.profitAfterTax == profitAfterTax)&&(identical(other.netProfit, netProfit) || other.netProfit == netProfit)&&(identical(other.netProfitChangePercent, netProfitChangePercent) || other.netProfitChangePercent == netProfitChangePercent)&&(identical(other.expenseRecorded, expenseRecorded) || other.expenseRecorded == expenseRecorded)&&(identical(other.expenseCorrection, expenseCorrection) || other.expenseCorrection == expenseCorrection)&&(identical(other.netExpense, netExpense) || other.netExpense == netExpense)&&(identical(other.supplierPayables, supplierPayables) || other.supplierPayables == supplierPayables)&&(identical(other.creditSalesAmount, creditSalesAmount) || other.creditSalesAmount == creditSalesAmount)&&(identical(other.creditSalesPercentage, creditSalesPercentage) || other.creditSalesPercentage == creditSalesPercentage)&&(identical(other.paymentMix, paymentMix) || other.paymentMix == paymentMix)&&(identical(other.creditShareWarning, creditShareWarning) || other.creditShareWarning == creditShareWarning)&&(identical(other.runningLowStockCount, runningLowStockCount) || other.runningLowStockCount == runningLowStockCount)&&(identical(other.lowStockItemCount, lowStockItemCount) || other.lowStockItemCount == lowStockItemCount)&&(identical(other.criticalStockCount, criticalStockCount) || other.criticalStockCount == criticalStockCount)&&const DeepCollectionEquality().equals(other.rankedShortageList, rankedShortageList)&&(identical(other.highestDueCustomer, highestDueCustomer) || other.highestDueCustomer == highestDueCustomer)&&const DeepCollectionEquality().equals(other.topFiveDueCustomers, topFiveDueCustomers)&&const DeepCollectionEquality().equals(other.alerts, alerts)&&const DeepCollectionEquality().equals(other.salesTrendSeries, salesTrendSeries)&&const DeepCollectionEquality().equals(other.revenueVsExpenses, revenueVsExpenses)&&const DeepCollectionEquality().equals(other.profitTrendSeries, profitTrendSeries)&&const DeepCollectionEquality().equals(other.paymentMixTrendSeries, paymentMixTrendSeries)&&(identical(other.previousPeriodSummary, previousPeriodSummary) || other.previousPeriodSummary == previousPeriodSummary)&&const DeepCollectionEquality().equals(other.latestSales, latestSales)&&(identical(other.stockValue, stockValue) || other.stockValue == stockValue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,generatedAt,startDate,endDate,salesCount,salesRevenue,hasNoSalesActivity,customerCreditDue,salesBooked,netSalesBooked,wastageCost,cashCollected,profitBeforeTax,profitAfterTax,netProfit,netProfitChangePercent,expenseRecorded,expenseCorrection,netExpense,supplierPayables,creditSalesAmount,creditSalesPercentage,paymentMix,creditShareWarning,runningLowStockCount,lowStockItemCount,criticalStockCount,const DeepCollectionEquality().hash(rankedShortageList),highestDueCustomer,const DeepCollectionEquality().hash(topFiveDueCustomers),const DeepCollectionEquality().hash(alerts),const DeepCollectionEquality().hash(salesTrendSeries),const DeepCollectionEquality().hash(revenueVsExpenses),const DeepCollectionEquality().hash(profitTrendSeries),const DeepCollectionEquality().hash(paymentMixTrendSeries),previousPeriodSummary,const DeepCollectionEquality().hash(latestSales),stockValue]);

@override
String toString() {
  return 'DashboardDto(generatedAt: $generatedAt, startDate: $startDate, endDate: $endDate, salesCount: $salesCount, salesRevenue: $salesRevenue, hasNoSalesActivity: $hasNoSalesActivity, customerCreditDue: $customerCreditDue, salesBooked: $salesBooked, netSalesBooked: $netSalesBooked, wastageCost: $wastageCost, cashCollected: $cashCollected, profitBeforeTax: $profitBeforeTax, profitAfterTax: $profitAfterTax, netProfit: $netProfit, netProfitChangePercent: $netProfitChangePercent, expenseRecorded: $expenseRecorded, expenseCorrection: $expenseCorrection, netExpense: $netExpense, supplierPayables: $supplierPayables, creditSalesAmount: $creditSalesAmount, creditSalesPercentage: $creditSalesPercentage, paymentMix: $paymentMix, creditShareWarning: $creditShareWarning, runningLowStockCount: $runningLowStockCount, lowStockItemCount: $lowStockItemCount, criticalStockCount: $criticalStockCount, rankedShortageList: $rankedShortageList, highestDueCustomer: $highestDueCustomer, topFiveDueCustomers: $topFiveDueCustomers, alerts: $alerts, salesTrendSeries: $salesTrendSeries, revenueVsExpenses: $revenueVsExpenses, profitTrendSeries: $profitTrendSeries, paymentMixTrendSeries: $paymentMixTrendSeries, previousPeriodSummary: $previousPeriodSummary, latestSales: $latestSales, stockValue: $stockValue)';
}


}

/// @nodoc
abstract mixin class $DashboardDtoCopyWith<$Res>  {
  factory $DashboardDtoCopyWith(DashboardDto value, $Res Function(DashboardDto) _then) = _$DashboardDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'generatedAt') String generatedAt,@JsonKey(name: 'startDate') String startDate,@JsonKey(name: 'endDate') String endDate,@JsonKey(name: 'salesCount') int salesCount,@JsonKey(name: 'salesRevenue') double? salesRevenue,@JsonKey(name: 'hasNoSalesActivity') bool hasNoSalesActivity,@JsonKey(name: 'customerCreditDue') double customerCreditDue,@JsonKey(name: 'salesBooked') double? salesBooked,@JsonKey(name: 'netSalesBooked') double? netSalesBooked,@JsonKey(name: 'wastageCost') double? wastageCost,@JsonKey(name: 'cashCollected') double? cashCollected,@JsonKey(name: 'profitBeforeTax') double? profitBeforeTax,@JsonKey(name: 'profitAfterTax') double? profitAfterTax,@JsonKey(name: 'netProfit') double? netProfit,@JsonKey(name: 'netProfitChangePercent') double? netProfitChangePercent,@JsonKey(name: 'expenseRecorded') double? expenseRecorded,@JsonKey(name: 'expenseCorrection') double? expenseCorrection,@JsonKey(name: 'netExpense') double? netExpense,@JsonKey(name: 'supplierPayables') double supplierPayables,@JsonKey(name: 'creditSalesAmount') double? creditSalesAmount,@JsonKey(name: 'creditSalesPercentage') double? creditSalesPercentage,@JsonKey(name: 'paymentMix') DashboardPaymentMixDto? paymentMix,@JsonKey(name: 'creditShareWarning') bool? creditShareWarning,@JsonKey(name: 'runningLowStockCount') int runningLowStockCount,@JsonKey(name: 'lowStockItemCount') int lowStockItemCount,@JsonKey(name: 'criticalStockCount') int criticalStockCount,@JsonKey(name: 'rankedShortageList') List<DashboardStockShortageDto> rankedShortageList,@JsonKey(name: 'highestDueCustomer') DashboardCustomerDueDto? highestDueCustomer,@JsonKey(name: 'topFiveDueCustomers') List<DashboardCustomerDueDto>? topFiveDueCustomers,@JsonKey(name: 'alerts') List<DashboardAlertDto> alerts,@JsonKey(name: 'salesTrendSeries') List<DashboardSalesTrendPointDto>? salesTrendSeries,@JsonKey(name: 'revenueVsExpenses') List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses,@JsonKey(name: 'profitTrendSeries') List<DashboardProfitTrendPointDto>? profitTrendSeries,@JsonKey(name: 'paymentMixTrendSeries') List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries,@JsonKey(name: 'previousPeriodSummary') DashboardPreviousPeriodSummaryDto? previousPeriodSummary,@JsonKey(name: 'latestSales') List<DashboardLatestSaleDto> latestSales,@JsonKey(name: 'stockValue') double? stockValue
});


$DashboardPaymentMixDtoCopyWith<$Res>? get paymentMix;$DashboardCustomerDueDtoCopyWith<$Res>? get highestDueCustomer;$DashboardPreviousPeriodSummaryDtoCopyWith<$Res>? get previousPeriodSummary;

}
/// @nodoc
class _$DashboardDtoCopyWithImpl<$Res>
    implements $DashboardDtoCopyWith<$Res> {
  _$DashboardDtoCopyWithImpl(this._self, this._then);

  final DashboardDto _self;
  final $Res Function(DashboardDto) _then;

/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? generatedAt = null,Object? startDate = null,Object? endDate = null,Object? salesCount = null,Object? salesRevenue = freezed,Object? hasNoSalesActivity = null,Object? customerCreditDue = null,Object? salesBooked = freezed,Object? netSalesBooked = freezed,Object? wastageCost = freezed,Object? cashCollected = freezed,Object? profitBeforeTax = freezed,Object? profitAfterTax = freezed,Object? netProfit = freezed,Object? netProfitChangePercent = freezed,Object? expenseRecorded = freezed,Object? expenseCorrection = freezed,Object? netExpense = freezed,Object? supplierPayables = null,Object? creditSalesAmount = freezed,Object? creditSalesPercentage = freezed,Object? paymentMix = freezed,Object? creditShareWarning = freezed,Object? runningLowStockCount = null,Object? lowStockItemCount = null,Object? criticalStockCount = null,Object? rankedShortageList = null,Object? highestDueCustomer = freezed,Object? topFiveDueCustomers = freezed,Object? alerts = null,Object? salesTrendSeries = freezed,Object? revenueVsExpenses = freezed,Object? profitTrendSeries = freezed,Object? paymentMixTrendSeries = freezed,Object? previousPeriodSummary = freezed,Object? latestSales = null,Object? stockValue = freezed,}) {
  return _then(_self.copyWith(
generatedAt: null == generatedAt ? _self.generatedAt : generatedAt // ignore: cast_nullable_to_non_nullable
as String,startDate: null == startDate ? _self.startDate : startDate // ignore: cast_nullable_to_non_nullable
as String,endDate: null == endDate ? _self.endDate : endDate // ignore: cast_nullable_to_non_nullable
as String,salesCount: null == salesCount ? _self.salesCount : salesCount // ignore: cast_nullable_to_non_nullable
as int,salesRevenue: freezed == salesRevenue ? _self.salesRevenue : salesRevenue // ignore: cast_nullable_to_non_nullable
as double?,hasNoSalesActivity: null == hasNoSalesActivity ? _self.hasNoSalesActivity : hasNoSalesActivity // ignore: cast_nullable_to_non_nullable
as bool,customerCreditDue: null == customerCreditDue ? _self.customerCreditDue : customerCreditDue // ignore: cast_nullable_to_non_nullable
as double,salesBooked: freezed == salesBooked ? _self.salesBooked : salesBooked // ignore: cast_nullable_to_non_nullable
as double?,netSalesBooked: freezed == netSalesBooked ? _self.netSalesBooked : netSalesBooked // ignore: cast_nullable_to_non_nullable
as double?,wastageCost: freezed == wastageCost ? _self.wastageCost : wastageCost // ignore: cast_nullable_to_non_nullable
as double?,cashCollected: freezed == cashCollected ? _self.cashCollected : cashCollected // ignore: cast_nullable_to_non_nullable
as double?,profitBeforeTax: freezed == profitBeforeTax ? _self.profitBeforeTax : profitBeforeTax // ignore: cast_nullable_to_non_nullable
as double?,profitAfterTax: freezed == profitAfterTax ? _self.profitAfterTax : profitAfterTax // ignore: cast_nullable_to_non_nullable
as double?,netProfit: freezed == netProfit ? _self.netProfit : netProfit // ignore: cast_nullable_to_non_nullable
as double?,netProfitChangePercent: freezed == netProfitChangePercent ? _self.netProfitChangePercent : netProfitChangePercent // ignore: cast_nullable_to_non_nullable
as double?,expenseRecorded: freezed == expenseRecorded ? _self.expenseRecorded : expenseRecorded // ignore: cast_nullable_to_non_nullable
as double?,expenseCorrection: freezed == expenseCorrection ? _self.expenseCorrection : expenseCorrection // ignore: cast_nullable_to_non_nullable
as double?,netExpense: freezed == netExpense ? _self.netExpense : netExpense // ignore: cast_nullable_to_non_nullable
as double?,supplierPayables: null == supplierPayables ? _self.supplierPayables : supplierPayables // ignore: cast_nullable_to_non_nullable
as double,creditSalesAmount: freezed == creditSalesAmount ? _self.creditSalesAmount : creditSalesAmount // ignore: cast_nullable_to_non_nullable
as double?,creditSalesPercentage: freezed == creditSalesPercentage ? _self.creditSalesPercentage : creditSalesPercentage // ignore: cast_nullable_to_non_nullable
as double?,paymentMix: freezed == paymentMix ? _self.paymentMix : paymentMix // ignore: cast_nullable_to_non_nullable
as DashboardPaymentMixDto?,creditShareWarning: freezed == creditShareWarning ? _self.creditShareWarning : creditShareWarning // ignore: cast_nullable_to_non_nullable
as bool?,runningLowStockCount: null == runningLowStockCount ? _self.runningLowStockCount : runningLowStockCount // ignore: cast_nullable_to_non_nullable
as int,lowStockItemCount: null == lowStockItemCount ? _self.lowStockItemCount : lowStockItemCount // ignore: cast_nullable_to_non_nullable
as int,criticalStockCount: null == criticalStockCount ? _self.criticalStockCount : criticalStockCount // ignore: cast_nullable_to_non_nullable
as int,rankedShortageList: null == rankedShortageList ? _self.rankedShortageList : rankedShortageList // ignore: cast_nullable_to_non_nullable
as List<DashboardStockShortageDto>,highestDueCustomer: freezed == highestDueCustomer ? _self.highestDueCustomer : highestDueCustomer // ignore: cast_nullable_to_non_nullable
as DashboardCustomerDueDto?,topFiveDueCustomers: freezed == topFiveDueCustomers ? _self.topFiveDueCustomers : topFiveDueCustomers // ignore: cast_nullable_to_non_nullable
as List<DashboardCustomerDueDto>?,alerts: null == alerts ? _self.alerts : alerts // ignore: cast_nullable_to_non_nullable
as List<DashboardAlertDto>,salesTrendSeries: freezed == salesTrendSeries ? _self.salesTrendSeries : salesTrendSeries // ignore: cast_nullable_to_non_nullable
as List<DashboardSalesTrendPointDto>?,revenueVsExpenses: freezed == revenueVsExpenses ? _self.revenueVsExpenses : revenueVsExpenses // ignore: cast_nullable_to_non_nullable
as List<DashboardRevenueVsExpensesPointDto>?,profitTrendSeries: freezed == profitTrendSeries ? _self.profitTrendSeries : profitTrendSeries // ignore: cast_nullable_to_non_nullable
as List<DashboardProfitTrendPointDto>?,paymentMixTrendSeries: freezed == paymentMixTrendSeries ? _self.paymentMixTrendSeries : paymentMixTrendSeries // ignore: cast_nullable_to_non_nullable
as List<DashboardPaymentMixTrendPointDto>?,previousPeriodSummary: freezed == previousPeriodSummary ? _self.previousPeriodSummary : previousPeriodSummary // ignore: cast_nullable_to_non_nullable
as DashboardPreviousPeriodSummaryDto?,latestSales: null == latestSales ? _self.latestSales : latestSales // ignore: cast_nullable_to_non_nullable
as List<DashboardLatestSaleDto>,stockValue: freezed == stockValue ? _self.stockValue : stockValue // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}
/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$DashboardPaymentMixDtoCopyWith<$Res>? get paymentMix {
    if (_self.paymentMix == null) {
    return null;
  }

  return $DashboardPaymentMixDtoCopyWith<$Res>(_self.paymentMix!, (value) {
    return _then(_self.copyWith(paymentMix: value));
  });
}/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$DashboardCustomerDueDtoCopyWith<$Res>? get highestDueCustomer {
    if (_self.highestDueCustomer == null) {
    return null;
  }

  return $DashboardCustomerDueDtoCopyWith<$Res>(_self.highestDueCustomer!, (value) {
    return _then(_self.copyWith(highestDueCustomer: value));
  });
}/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$DashboardPreviousPeriodSummaryDtoCopyWith<$Res>? get previousPeriodSummary {
    if (_self.previousPeriodSummary == null) {
    return null;
  }

  return $DashboardPreviousPeriodSummaryDtoCopyWith<$Res>(_self.previousPeriodSummary!, (value) {
    return _then(_self.copyWith(previousPeriodSummary: value));
  });
}
}


/// Adds pattern-matching-related methods to [DashboardDto].
extension DashboardDtoPatterns on DashboardDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'generatedAt')  String generatedAt, @JsonKey(name: 'startDate')  String startDate, @JsonKey(name: 'endDate')  String endDate, @JsonKey(name: 'salesCount')  int salesCount, @JsonKey(name: 'salesRevenue')  double? salesRevenue, @JsonKey(name: 'hasNoSalesActivity')  bool hasNoSalesActivity, @JsonKey(name: 'customerCreditDue')  double customerCreditDue, @JsonKey(name: 'salesBooked')  double? salesBooked, @JsonKey(name: 'netSalesBooked')  double? netSalesBooked, @JsonKey(name: 'wastageCost')  double? wastageCost, @JsonKey(name: 'cashCollected')  double? cashCollected, @JsonKey(name: 'profitBeforeTax')  double? profitBeforeTax, @JsonKey(name: 'profitAfterTax')  double? profitAfterTax, @JsonKey(name: 'netProfit')  double? netProfit, @JsonKey(name: 'netProfitChangePercent')  double? netProfitChangePercent, @JsonKey(name: 'expenseRecorded')  double? expenseRecorded, @JsonKey(name: 'expenseCorrection')  double? expenseCorrection, @JsonKey(name: 'netExpense')  double? netExpense, @JsonKey(name: 'supplierPayables')  double supplierPayables, @JsonKey(name: 'creditSalesAmount')  double? creditSalesAmount, @JsonKey(name: 'creditSalesPercentage')  double? creditSalesPercentage, @JsonKey(name: 'paymentMix')  DashboardPaymentMixDto? paymentMix, @JsonKey(name: 'creditShareWarning')  bool? creditShareWarning, @JsonKey(name: 'runningLowStockCount')  int runningLowStockCount, @JsonKey(name: 'lowStockItemCount')  int lowStockItemCount, @JsonKey(name: 'criticalStockCount')  int criticalStockCount, @JsonKey(name: 'rankedShortageList')  List<DashboardStockShortageDto> rankedShortageList, @JsonKey(name: 'highestDueCustomer')  DashboardCustomerDueDto? highestDueCustomer, @JsonKey(name: 'topFiveDueCustomers')  List<DashboardCustomerDueDto>? topFiveDueCustomers, @JsonKey(name: 'alerts')  List<DashboardAlertDto> alerts, @JsonKey(name: 'salesTrendSeries')  List<DashboardSalesTrendPointDto>? salesTrendSeries, @JsonKey(name: 'revenueVsExpenses')  List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses, @JsonKey(name: 'profitTrendSeries')  List<DashboardProfitTrendPointDto>? profitTrendSeries, @JsonKey(name: 'paymentMixTrendSeries')  List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries, @JsonKey(name: 'previousPeriodSummary')  DashboardPreviousPeriodSummaryDto? previousPeriodSummary, @JsonKey(name: 'latestSales')  List<DashboardLatestSaleDto> latestSales, @JsonKey(name: 'stockValue')  double? stockValue)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardDto() when $default != null:
return $default(_that.generatedAt,_that.startDate,_that.endDate,_that.salesCount,_that.salesRevenue,_that.hasNoSalesActivity,_that.customerCreditDue,_that.salesBooked,_that.netSalesBooked,_that.wastageCost,_that.cashCollected,_that.profitBeforeTax,_that.profitAfterTax,_that.netProfit,_that.netProfitChangePercent,_that.expenseRecorded,_that.expenseCorrection,_that.netExpense,_that.supplierPayables,_that.creditSalesAmount,_that.creditSalesPercentage,_that.paymentMix,_that.creditShareWarning,_that.runningLowStockCount,_that.lowStockItemCount,_that.criticalStockCount,_that.rankedShortageList,_that.highestDueCustomer,_that.topFiveDueCustomers,_that.alerts,_that.salesTrendSeries,_that.revenueVsExpenses,_that.profitTrendSeries,_that.paymentMixTrendSeries,_that.previousPeriodSummary,_that.latestSales,_that.stockValue);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'generatedAt')  String generatedAt, @JsonKey(name: 'startDate')  String startDate, @JsonKey(name: 'endDate')  String endDate, @JsonKey(name: 'salesCount')  int salesCount, @JsonKey(name: 'salesRevenue')  double? salesRevenue, @JsonKey(name: 'hasNoSalesActivity')  bool hasNoSalesActivity, @JsonKey(name: 'customerCreditDue')  double customerCreditDue, @JsonKey(name: 'salesBooked')  double? salesBooked, @JsonKey(name: 'netSalesBooked')  double? netSalesBooked, @JsonKey(name: 'wastageCost')  double? wastageCost, @JsonKey(name: 'cashCollected')  double? cashCollected, @JsonKey(name: 'profitBeforeTax')  double? profitBeforeTax, @JsonKey(name: 'profitAfterTax')  double? profitAfterTax, @JsonKey(name: 'netProfit')  double? netProfit, @JsonKey(name: 'netProfitChangePercent')  double? netProfitChangePercent, @JsonKey(name: 'expenseRecorded')  double? expenseRecorded, @JsonKey(name: 'expenseCorrection')  double? expenseCorrection, @JsonKey(name: 'netExpense')  double? netExpense, @JsonKey(name: 'supplierPayables')  double supplierPayables, @JsonKey(name: 'creditSalesAmount')  double? creditSalesAmount, @JsonKey(name: 'creditSalesPercentage')  double? creditSalesPercentage, @JsonKey(name: 'paymentMix')  DashboardPaymentMixDto? paymentMix, @JsonKey(name: 'creditShareWarning')  bool? creditShareWarning, @JsonKey(name: 'runningLowStockCount')  int runningLowStockCount, @JsonKey(name: 'lowStockItemCount')  int lowStockItemCount, @JsonKey(name: 'criticalStockCount')  int criticalStockCount, @JsonKey(name: 'rankedShortageList')  List<DashboardStockShortageDto> rankedShortageList, @JsonKey(name: 'highestDueCustomer')  DashboardCustomerDueDto? highestDueCustomer, @JsonKey(name: 'topFiveDueCustomers')  List<DashboardCustomerDueDto>? topFiveDueCustomers, @JsonKey(name: 'alerts')  List<DashboardAlertDto> alerts, @JsonKey(name: 'salesTrendSeries')  List<DashboardSalesTrendPointDto>? salesTrendSeries, @JsonKey(name: 'revenueVsExpenses')  List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses, @JsonKey(name: 'profitTrendSeries')  List<DashboardProfitTrendPointDto>? profitTrendSeries, @JsonKey(name: 'paymentMixTrendSeries')  List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries, @JsonKey(name: 'previousPeriodSummary')  DashboardPreviousPeriodSummaryDto? previousPeriodSummary, @JsonKey(name: 'latestSales')  List<DashboardLatestSaleDto> latestSales, @JsonKey(name: 'stockValue')  double? stockValue)  $default,) {final _that = this;
switch (_that) {
case _DashboardDto():
return $default(_that.generatedAt,_that.startDate,_that.endDate,_that.salesCount,_that.salesRevenue,_that.hasNoSalesActivity,_that.customerCreditDue,_that.salesBooked,_that.netSalesBooked,_that.wastageCost,_that.cashCollected,_that.profitBeforeTax,_that.profitAfterTax,_that.netProfit,_that.netProfitChangePercent,_that.expenseRecorded,_that.expenseCorrection,_that.netExpense,_that.supplierPayables,_that.creditSalesAmount,_that.creditSalesPercentage,_that.paymentMix,_that.creditShareWarning,_that.runningLowStockCount,_that.lowStockItemCount,_that.criticalStockCount,_that.rankedShortageList,_that.highestDueCustomer,_that.topFiveDueCustomers,_that.alerts,_that.salesTrendSeries,_that.revenueVsExpenses,_that.profitTrendSeries,_that.paymentMixTrendSeries,_that.previousPeriodSummary,_that.latestSales,_that.stockValue);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'generatedAt')  String generatedAt, @JsonKey(name: 'startDate')  String startDate, @JsonKey(name: 'endDate')  String endDate, @JsonKey(name: 'salesCount')  int salesCount, @JsonKey(name: 'salesRevenue')  double? salesRevenue, @JsonKey(name: 'hasNoSalesActivity')  bool hasNoSalesActivity, @JsonKey(name: 'customerCreditDue')  double customerCreditDue, @JsonKey(name: 'salesBooked')  double? salesBooked, @JsonKey(name: 'netSalesBooked')  double? netSalesBooked, @JsonKey(name: 'wastageCost')  double? wastageCost, @JsonKey(name: 'cashCollected')  double? cashCollected, @JsonKey(name: 'profitBeforeTax')  double? profitBeforeTax, @JsonKey(name: 'profitAfterTax')  double? profitAfterTax, @JsonKey(name: 'netProfit')  double? netProfit, @JsonKey(name: 'netProfitChangePercent')  double? netProfitChangePercent, @JsonKey(name: 'expenseRecorded')  double? expenseRecorded, @JsonKey(name: 'expenseCorrection')  double? expenseCorrection, @JsonKey(name: 'netExpense')  double? netExpense, @JsonKey(name: 'supplierPayables')  double supplierPayables, @JsonKey(name: 'creditSalesAmount')  double? creditSalesAmount, @JsonKey(name: 'creditSalesPercentage')  double? creditSalesPercentage, @JsonKey(name: 'paymentMix')  DashboardPaymentMixDto? paymentMix, @JsonKey(name: 'creditShareWarning')  bool? creditShareWarning, @JsonKey(name: 'runningLowStockCount')  int runningLowStockCount, @JsonKey(name: 'lowStockItemCount')  int lowStockItemCount, @JsonKey(name: 'criticalStockCount')  int criticalStockCount, @JsonKey(name: 'rankedShortageList')  List<DashboardStockShortageDto> rankedShortageList, @JsonKey(name: 'highestDueCustomer')  DashboardCustomerDueDto? highestDueCustomer, @JsonKey(name: 'topFiveDueCustomers')  List<DashboardCustomerDueDto>? topFiveDueCustomers, @JsonKey(name: 'alerts')  List<DashboardAlertDto> alerts, @JsonKey(name: 'salesTrendSeries')  List<DashboardSalesTrendPointDto>? salesTrendSeries, @JsonKey(name: 'revenueVsExpenses')  List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses, @JsonKey(name: 'profitTrendSeries')  List<DashboardProfitTrendPointDto>? profitTrendSeries, @JsonKey(name: 'paymentMixTrendSeries')  List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries, @JsonKey(name: 'previousPeriodSummary')  DashboardPreviousPeriodSummaryDto? previousPeriodSummary, @JsonKey(name: 'latestSales')  List<DashboardLatestSaleDto> latestSales, @JsonKey(name: 'stockValue')  double? stockValue)?  $default,) {final _that = this;
switch (_that) {
case _DashboardDto() when $default != null:
return $default(_that.generatedAt,_that.startDate,_that.endDate,_that.salesCount,_that.salesRevenue,_that.hasNoSalesActivity,_that.customerCreditDue,_that.salesBooked,_that.netSalesBooked,_that.wastageCost,_that.cashCollected,_that.profitBeforeTax,_that.profitAfterTax,_that.netProfit,_that.netProfitChangePercent,_that.expenseRecorded,_that.expenseCorrection,_that.netExpense,_that.supplierPayables,_that.creditSalesAmount,_that.creditSalesPercentage,_that.paymentMix,_that.creditShareWarning,_that.runningLowStockCount,_that.lowStockItemCount,_that.criticalStockCount,_that.rankedShortageList,_that.highestDueCustomer,_that.topFiveDueCustomers,_that.alerts,_that.salesTrendSeries,_that.revenueVsExpenses,_that.profitTrendSeries,_that.paymentMixTrendSeries,_that.previousPeriodSummary,_that.latestSales,_that.stockValue);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardDto implements DashboardDto {
  const _DashboardDto({@JsonKey(name: 'generatedAt') required this.generatedAt, @JsonKey(name: 'startDate') required this.startDate, @JsonKey(name: 'endDate') required this.endDate, @JsonKey(name: 'salesCount') required this.salesCount, @JsonKey(name: 'salesRevenue') this.salesRevenue, @JsonKey(name: 'hasNoSalesActivity') required this.hasNoSalesActivity, @JsonKey(name: 'customerCreditDue') required this.customerCreditDue, @JsonKey(name: 'salesBooked') this.salesBooked, @JsonKey(name: 'netSalesBooked') this.netSalesBooked, @JsonKey(name: 'wastageCost') this.wastageCost, @JsonKey(name: 'cashCollected') this.cashCollected, @JsonKey(name: 'profitBeforeTax') this.profitBeforeTax, @JsonKey(name: 'profitAfterTax') this.profitAfterTax, @JsonKey(name: 'netProfit') this.netProfit, @JsonKey(name: 'netProfitChangePercent') this.netProfitChangePercent, @JsonKey(name: 'expenseRecorded') this.expenseRecorded, @JsonKey(name: 'expenseCorrection') this.expenseCorrection, @JsonKey(name: 'netExpense') this.netExpense, @JsonKey(name: 'supplierPayables') required this.supplierPayables, @JsonKey(name: 'creditSalesAmount') this.creditSalesAmount, @JsonKey(name: 'creditSalesPercentage') this.creditSalesPercentage, @JsonKey(name: 'paymentMix') this.paymentMix, @JsonKey(name: 'creditShareWarning') this.creditShareWarning, @JsonKey(name: 'runningLowStockCount') required this.runningLowStockCount, @JsonKey(name: 'lowStockItemCount') required this.lowStockItemCount, @JsonKey(name: 'criticalStockCount') required this.criticalStockCount, @JsonKey(name: 'rankedShortageList') final  List<DashboardStockShortageDto> rankedShortageList = const <DashboardStockShortageDto>[], @JsonKey(name: 'highestDueCustomer') this.highestDueCustomer, @JsonKey(name: 'topFiveDueCustomers') final  List<DashboardCustomerDueDto>? topFiveDueCustomers, @JsonKey(name: 'alerts') final  List<DashboardAlertDto> alerts = const <DashboardAlertDto>[], @JsonKey(name: 'salesTrendSeries') final  List<DashboardSalesTrendPointDto>? salesTrendSeries, @JsonKey(name: 'revenueVsExpenses') final  List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses, @JsonKey(name: 'profitTrendSeries') final  List<DashboardProfitTrendPointDto>? profitTrendSeries, @JsonKey(name: 'paymentMixTrendSeries') final  List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries, @JsonKey(name: 'previousPeriodSummary') this.previousPeriodSummary, @JsonKey(name: 'latestSales') final  List<DashboardLatestSaleDto> latestSales = const <DashboardLatestSaleDto>[], @JsonKey(name: 'stockValue') this.stockValue}): _rankedShortageList = rankedShortageList,_topFiveDueCustomers = topFiveDueCustomers,_alerts = alerts,_salesTrendSeries = salesTrendSeries,_revenueVsExpenses = revenueVsExpenses,_profitTrendSeries = profitTrendSeries,_paymentMixTrendSeries = paymentMixTrendSeries,_latestSales = latestSales;
  factory _DashboardDto.fromJson(Map<String, dynamic> json) => _$DashboardDtoFromJson(json);

@override@JsonKey(name: 'generatedAt') final  String generatedAt;
@override@JsonKey(name: 'startDate') final  String startDate;
@override@JsonKey(name: 'endDate') final  String endDate;
@override@JsonKey(name: 'salesCount') final  int salesCount;
@override@JsonKey(name: 'salesRevenue') final  double? salesRevenue;
@override@JsonKey(name: 'hasNoSalesActivity') final  bool hasNoSalesActivity;
@override@JsonKey(name: 'customerCreditDue') final  double customerCreditDue;
@override@JsonKey(name: 'salesBooked') final  double? salesBooked;
@override@JsonKey(name: 'netSalesBooked') final  double? netSalesBooked;
@override@JsonKey(name: 'wastageCost') final  double? wastageCost;
@override@JsonKey(name: 'cashCollected') final  double? cashCollected;
@override@JsonKey(name: 'profitBeforeTax') final  double? profitBeforeTax;
@override@JsonKey(name: 'profitAfterTax') final  double? profitAfterTax;
@override@JsonKey(name: 'netProfit') final  double? netProfit;
@override@JsonKey(name: 'netProfitChangePercent') final  double? netProfitChangePercent;
@override@JsonKey(name: 'expenseRecorded') final  double? expenseRecorded;
@override@JsonKey(name: 'expenseCorrection') final  double? expenseCorrection;
@override@JsonKey(name: 'netExpense') final  double? netExpense;
@override@JsonKey(name: 'supplierPayables') final  double supplierPayables;
@override@JsonKey(name: 'creditSalesAmount') final  double? creditSalesAmount;
@override@JsonKey(name: 'creditSalesPercentage') final  double? creditSalesPercentage;
@override@JsonKey(name: 'paymentMix') final  DashboardPaymentMixDto? paymentMix;
@override@JsonKey(name: 'creditShareWarning') final  bool? creditShareWarning;
@override@JsonKey(name: 'runningLowStockCount') final  int runningLowStockCount;
@override@JsonKey(name: 'lowStockItemCount') final  int lowStockItemCount;
@override@JsonKey(name: 'criticalStockCount') final  int criticalStockCount;
 final  List<DashboardStockShortageDto> _rankedShortageList;
@override@JsonKey(name: 'rankedShortageList') List<DashboardStockShortageDto> get rankedShortageList {
  if (_rankedShortageList is EqualUnmodifiableListView) return _rankedShortageList;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_rankedShortageList);
}

@override@JsonKey(name: 'highestDueCustomer') final  DashboardCustomerDueDto? highestDueCustomer;
 final  List<DashboardCustomerDueDto>? _topFiveDueCustomers;
@override@JsonKey(name: 'topFiveDueCustomers') List<DashboardCustomerDueDto>? get topFiveDueCustomers {
  final value = _topFiveDueCustomers;
  if (value == null) return null;
  if (_topFiveDueCustomers is EqualUnmodifiableListView) return _topFiveDueCustomers;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}

 final  List<DashboardAlertDto> _alerts;
@override@JsonKey(name: 'alerts') List<DashboardAlertDto> get alerts {
  if (_alerts is EqualUnmodifiableListView) return _alerts;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_alerts);
}

 final  List<DashboardSalesTrendPointDto>? _salesTrendSeries;
@override@JsonKey(name: 'salesTrendSeries') List<DashboardSalesTrendPointDto>? get salesTrendSeries {
  final value = _salesTrendSeries;
  if (value == null) return null;
  if (_salesTrendSeries is EqualUnmodifiableListView) return _salesTrendSeries;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}

 final  List<DashboardRevenueVsExpensesPointDto>? _revenueVsExpenses;
@override@JsonKey(name: 'revenueVsExpenses') List<DashboardRevenueVsExpensesPointDto>? get revenueVsExpenses {
  final value = _revenueVsExpenses;
  if (value == null) return null;
  if (_revenueVsExpenses is EqualUnmodifiableListView) return _revenueVsExpenses;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}

 final  List<DashboardProfitTrendPointDto>? _profitTrendSeries;
@override@JsonKey(name: 'profitTrendSeries') List<DashboardProfitTrendPointDto>? get profitTrendSeries {
  final value = _profitTrendSeries;
  if (value == null) return null;
  if (_profitTrendSeries is EqualUnmodifiableListView) return _profitTrendSeries;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}

 final  List<DashboardPaymentMixTrendPointDto>? _paymentMixTrendSeries;
@override@JsonKey(name: 'paymentMixTrendSeries') List<DashboardPaymentMixTrendPointDto>? get paymentMixTrendSeries {
  final value = _paymentMixTrendSeries;
  if (value == null) return null;
  if (_paymentMixTrendSeries is EqualUnmodifiableListView) return _paymentMixTrendSeries;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}

@override@JsonKey(name: 'previousPeriodSummary') final  DashboardPreviousPeriodSummaryDto? previousPeriodSummary;
 final  List<DashboardLatestSaleDto> _latestSales;
@override@JsonKey(name: 'latestSales') List<DashboardLatestSaleDto> get latestSales {
  if (_latestSales is EqualUnmodifiableListView) return _latestSales;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_latestSales);
}

@override@JsonKey(name: 'stockValue') final  double? stockValue;

/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardDtoCopyWith<_DashboardDto> get copyWith => __$DashboardDtoCopyWithImpl<_DashboardDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardDto&&(identical(other.generatedAt, generatedAt) || other.generatedAt == generatedAt)&&(identical(other.startDate, startDate) || other.startDate == startDate)&&(identical(other.endDate, endDate) || other.endDate == endDate)&&(identical(other.salesCount, salesCount) || other.salesCount == salesCount)&&(identical(other.salesRevenue, salesRevenue) || other.salesRevenue == salesRevenue)&&(identical(other.hasNoSalesActivity, hasNoSalesActivity) || other.hasNoSalesActivity == hasNoSalesActivity)&&(identical(other.customerCreditDue, customerCreditDue) || other.customerCreditDue == customerCreditDue)&&(identical(other.salesBooked, salesBooked) || other.salesBooked == salesBooked)&&(identical(other.netSalesBooked, netSalesBooked) || other.netSalesBooked == netSalesBooked)&&(identical(other.wastageCost, wastageCost) || other.wastageCost == wastageCost)&&(identical(other.cashCollected, cashCollected) || other.cashCollected == cashCollected)&&(identical(other.profitBeforeTax, profitBeforeTax) || other.profitBeforeTax == profitBeforeTax)&&(identical(other.profitAfterTax, profitAfterTax) || other.profitAfterTax == profitAfterTax)&&(identical(other.netProfit, netProfit) || other.netProfit == netProfit)&&(identical(other.netProfitChangePercent, netProfitChangePercent) || other.netProfitChangePercent == netProfitChangePercent)&&(identical(other.expenseRecorded, expenseRecorded) || other.expenseRecorded == expenseRecorded)&&(identical(other.expenseCorrection, expenseCorrection) || other.expenseCorrection == expenseCorrection)&&(identical(other.netExpense, netExpense) || other.netExpense == netExpense)&&(identical(other.supplierPayables, supplierPayables) || other.supplierPayables == supplierPayables)&&(identical(other.creditSalesAmount, creditSalesAmount) || other.creditSalesAmount == creditSalesAmount)&&(identical(other.creditSalesPercentage, creditSalesPercentage) || other.creditSalesPercentage == creditSalesPercentage)&&(identical(other.paymentMix, paymentMix) || other.paymentMix == paymentMix)&&(identical(other.creditShareWarning, creditShareWarning) || other.creditShareWarning == creditShareWarning)&&(identical(other.runningLowStockCount, runningLowStockCount) || other.runningLowStockCount == runningLowStockCount)&&(identical(other.lowStockItemCount, lowStockItemCount) || other.lowStockItemCount == lowStockItemCount)&&(identical(other.criticalStockCount, criticalStockCount) || other.criticalStockCount == criticalStockCount)&&const DeepCollectionEquality().equals(other._rankedShortageList, _rankedShortageList)&&(identical(other.highestDueCustomer, highestDueCustomer) || other.highestDueCustomer == highestDueCustomer)&&const DeepCollectionEquality().equals(other._topFiveDueCustomers, _topFiveDueCustomers)&&const DeepCollectionEquality().equals(other._alerts, _alerts)&&const DeepCollectionEquality().equals(other._salesTrendSeries, _salesTrendSeries)&&const DeepCollectionEquality().equals(other._revenueVsExpenses, _revenueVsExpenses)&&const DeepCollectionEquality().equals(other._profitTrendSeries, _profitTrendSeries)&&const DeepCollectionEquality().equals(other._paymentMixTrendSeries, _paymentMixTrendSeries)&&(identical(other.previousPeriodSummary, previousPeriodSummary) || other.previousPeriodSummary == previousPeriodSummary)&&const DeepCollectionEquality().equals(other._latestSales, _latestSales)&&(identical(other.stockValue, stockValue) || other.stockValue == stockValue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,generatedAt,startDate,endDate,salesCount,salesRevenue,hasNoSalesActivity,customerCreditDue,salesBooked,netSalesBooked,wastageCost,cashCollected,profitBeforeTax,profitAfterTax,netProfit,netProfitChangePercent,expenseRecorded,expenseCorrection,netExpense,supplierPayables,creditSalesAmount,creditSalesPercentage,paymentMix,creditShareWarning,runningLowStockCount,lowStockItemCount,criticalStockCount,const DeepCollectionEquality().hash(_rankedShortageList),highestDueCustomer,const DeepCollectionEquality().hash(_topFiveDueCustomers),const DeepCollectionEquality().hash(_alerts),const DeepCollectionEquality().hash(_salesTrendSeries),const DeepCollectionEquality().hash(_revenueVsExpenses),const DeepCollectionEquality().hash(_profitTrendSeries),const DeepCollectionEquality().hash(_paymentMixTrendSeries),previousPeriodSummary,const DeepCollectionEquality().hash(_latestSales),stockValue]);

@override
String toString() {
  return 'DashboardDto(generatedAt: $generatedAt, startDate: $startDate, endDate: $endDate, salesCount: $salesCount, salesRevenue: $salesRevenue, hasNoSalesActivity: $hasNoSalesActivity, customerCreditDue: $customerCreditDue, salesBooked: $salesBooked, netSalesBooked: $netSalesBooked, wastageCost: $wastageCost, cashCollected: $cashCollected, profitBeforeTax: $profitBeforeTax, profitAfterTax: $profitAfterTax, netProfit: $netProfit, netProfitChangePercent: $netProfitChangePercent, expenseRecorded: $expenseRecorded, expenseCorrection: $expenseCorrection, netExpense: $netExpense, supplierPayables: $supplierPayables, creditSalesAmount: $creditSalesAmount, creditSalesPercentage: $creditSalesPercentage, paymentMix: $paymentMix, creditShareWarning: $creditShareWarning, runningLowStockCount: $runningLowStockCount, lowStockItemCount: $lowStockItemCount, criticalStockCount: $criticalStockCount, rankedShortageList: $rankedShortageList, highestDueCustomer: $highestDueCustomer, topFiveDueCustomers: $topFiveDueCustomers, alerts: $alerts, salesTrendSeries: $salesTrendSeries, revenueVsExpenses: $revenueVsExpenses, profitTrendSeries: $profitTrendSeries, paymentMixTrendSeries: $paymentMixTrendSeries, previousPeriodSummary: $previousPeriodSummary, latestSales: $latestSales, stockValue: $stockValue)';
}


}

/// @nodoc
abstract mixin class _$DashboardDtoCopyWith<$Res> implements $DashboardDtoCopyWith<$Res> {
  factory _$DashboardDtoCopyWith(_DashboardDto value, $Res Function(_DashboardDto) _then) = __$DashboardDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'generatedAt') String generatedAt,@JsonKey(name: 'startDate') String startDate,@JsonKey(name: 'endDate') String endDate,@JsonKey(name: 'salesCount') int salesCount,@JsonKey(name: 'salesRevenue') double? salesRevenue,@JsonKey(name: 'hasNoSalesActivity') bool hasNoSalesActivity,@JsonKey(name: 'customerCreditDue') double customerCreditDue,@JsonKey(name: 'salesBooked') double? salesBooked,@JsonKey(name: 'netSalesBooked') double? netSalesBooked,@JsonKey(name: 'wastageCost') double? wastageCost,@JsonKey(name: 'cashCollected') double? cashCollected,@JsonKey(name: 'profitBeforeTax') double? profitBeforeTax,@JsonKey(name: 'profitAfterTax') double? profitAfterTax,@JsonKey(name: 'netProfit') double? netProfit,@JsonKey(name: 'netProfitChangePercent') double? netProfitChangePercent,@JsonKey(name: 'expenseRecorded') double? expenseRecorded,@JsonKey(name: 'expenseCorrection') double? expenseCorrection,@JsonKey(name: 'netExpense') double? netExpense,@JsonKey(name: 'supplierPayables') double supplierPayables,@JsonKey(name: 'creditSalesAmount') double? creditSalesAmount,@JsonKey(name: 'creditSalesPercentage') double? creditSalesPercentage,@JsonKey(name: 'paymentMix') DashboardPaymentMixDto? paymentMix,@JsonKey(name: 'creditShareWarning') bool? creditShareWarning,@JsonKey(name: 'runningLowStockCount') int runningLowStockCount,@JsonKey(name: 'lowStockItemCount') int lowStockItemCount,@JsonKey(name: 'criticalStockCount') int criticalStockCount,@JsonKey(name: 'rankedShortageList') List<DashboardStockShortageDto> rankedShortageList,@JsonKey(name: 'highestDueCustomer') DashboardCustomerDueDto? highestDueCustomer,@JsonKey(name: 'topFiveDueCustomers') List<DashboardCustomerDueDto>? topFiveDueCustomers,@JsonKey(name: 'alerts') List<DashboardAlertDto> alerts,@JsonKey(name: 'salesTrendSeries') List<DashboardSalesTrendPointDto>? salesTrendSeries,@JsonKey(name: 'revenueVsExpenses') List<DashboardRevenueVsExpensesPointDto>? revenueVsExpenses,@JsonKey(name: 'profitTrendSeries') List<DashboardProfitTrendPointDto>? profitTrendSeries,@JsonKey(name: 'paymentMixTrendSeries') List<DashboardPaymentMixTrendPointDto>? paymentMixTrendSeries,@JsonKey(name: 'previousPeriodSummary') DashboardPreviousPeriodSummaryDto? previousPeriodSummary,@JsonKey(name: 'latestSales') List<DashboardLatestSaleDto> latestSales,@JsonKey(name: 'stockValue') double? stockValue
});


@override $DashboardPaymentMixDtoCopyWith<$Res>? get paymentMix;@override $DashboardCustomerDueDtoCopyWith<$Res>? get highestDueCustomer;@override $DashboardPreviousPeriodSummaryDtoCopyWith<$Res>? get previousPeriodSummary;

}
/// @nodoc
class __$DashboardDtoCopyWithImpl<$Res>
    implements _$DashboardDtoCopyWith<$Res> {
  __$DashboardDtoCopyWithImpl(this._self, this._then);

  final _DashboardDto _self;
  final $Res Function(_DashboardDto) _then;

/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? generatedAt = null,Object? startDate = null,Object? endDate = null,Object? salesCount = null,Object? salesRevenue = freezed,Object? hasNoSalesActivity = null,Object? customerCreditDue = null,Object? salesBooked = freezed,Object? netSalesBooked = freezed,Object? wastageCost = freezed,Object? cashCollected = freezed,Object? profitBeforeTax = freezed,Object? profitAfterTax = freezed,Object? netProfit = freezed,Object? netProfitChangePercent = freezed,Object? expenseRecorded = freezed,Object? expenseCorrection = freezed,Object? netExpense = freezed,Object? supplierPayables = null,Object? creditSalesAmount = freezed,Object? creditSalesPercentage = freezed,Object? paymentMix = freezed,Object? creditShareWarning = freezed,Object? runningLowStockCount = null,Object? lowStockItemCount = null,Object? criticalStockCount = null,Object? rankedShortageList = null,Object? highestDueCustomer = freezed,Object? topFiveDueCustomers = freezed,Object? alerts = null,Object? salesTrendSeries = freezed,Object? revenueVsExpenses = freezed,Object? profitTrendSeries = freezed,Object? paymentMixTrendSeries = freezed,Object? previousPeriodSummary = freezed,Object? latestSales = null,Object? stockValue = freezed,}) {
  return _then(_DashboardDto(
generatedAt: null == generatedAt ? _self.generatedAt : generatedAt // ignore: cast_nullable_to_non_nullable
as String,startDate: null == startDate ? _self.startDate : startDate // ignore: cast_nullable_to_non_nullable
as String,endDate: null == endDate ? _self.endDate : endDate // ignore: cast_nullable_to_non_nullable
as String,salesCount: null == salesCount ? _self.salesCount : salesCount // ignore: cast_nullable_to_non_nullable
as int,salesRevenue: freezed == salesRevenue ? _self.salesRevenue : salesRevenue // ignore: cast_nullable_to_non_nullable
as double?,hasNoSalesActivity: null == hasNoSalesActivity ? _self.hasNoSalesActivity : hasNoSalesActivity // ignore: cast_nullable_to_non_nullable
as bool,customerCreditDue: null == customerCreditDue ? _self.customerCreditDue : customerCreditDue // ignore: cast_nullable_to_non_nullable
as double,salesBooked: freezed == salesBooked ? _self.salesBooked : salesBooked // ignore: cast_nullable_to_non_nullable
as double?,netSalesBooked: freezed == netSalesBooked ? _self.netSalesBooked : netSalesBooked // ignore: cast_nullable_to_non_nullable
as double?,wastageCost: freezed == wastageCost ? _self.wastageCost : wastageCost // ignore: cast_nullable_to_non_nullable
as double?,cashCollected: freezed == cashCollected ? _self.cashCollected : cashCollected // ignore: cast_nullable_to_non_nullable
as double?,profitBeforeTax: freezed == profitBeforeTax ? _self.profitBeforeTax : profitBeforeTax // ignore: cast_nullable_to_non_nullable
as double?,profitAfterTax: freezed == profitAfterTax ? _self.profitAfterTax : profitAfterTax // ignore: cast_nullable_to_non_nullable
as double?,netProfit: freezed == netProfit ? _self.netProfit : netProfit // ignore: cast_nullable_to_non_nullable
as double?,netProfitChangePercent: freezed == netProfitChangePercent ? _self.netProfitChangePercent : netProfitChangePercent // ignore: cast_nullable_to_non_nullable
as double?,expenseRecorded: freezed == expenseRecorded ? _self.expenseRecorded : expenseRecorded // ignore: cast_nullable_to_non_nullable
as double?,expenseCorrection: freezed == expenseCorrection ? _self.expenseCorrection : expenseCorrection // ignore: cast_nullable_to_non_nullable
as double?,netExpense: freezed == netExpense ? _self.netExpense : netExpense // ignore: cast_nullable_to_non_nullable
as double?,supplierPayables: null == supplierPayables ? _self.supplierPayables : supplierPayables // ignore: cast_nullable_to_non_nullable
as double,creditSalesAmount: freezed == creditSalesAmount ? _self.creditSalesAmount : creditSalesAmount // ignore: cast_nullable_to_non_nullable
as double?,creditSalesPercentage: freezed == creditSalesPercentage ? _self.creditSalesPercentage : creditSalesPercentage // ignore: cast_nullable_to_non_nullable
as double?,paymentMix: freezed == paymentMix ? _self.paymentMix : paymentMix // ignore: cast_nullable_to_non_nullable
as DashboardPaymentMixDto?,creditShareWarning: freezed == creditShareWarning ? _self.creditShareWarning : creditShareWarning // ignore: cast_nullable_to_non_nullable
as bool?,runningLowStockCount: null == runningLowStockCount ? _self.runningLowStockCount : runningLowStockCount // ignore: cast_nullable_to_non_nullable
as int,lowStockItemCount: null == lowStockItemCount ? _self.lowStockItemCount : lowStockItemCount // ignore: cast_nullable_to_non_nullable
as int,criticalStockCount: null == criticalStockCount ? _self.criticalStockCount : criticalStockCount // ignore: cast_nullable_to_non_nullable
as int,rankedShortageList: null == rankedShortageList ? _self._rankedShortageList : rankedShortageList // ignore: cast_nullable_to_non_nullable
as List<DashboardStockShortageDto>,highestDueCustomer: freezed == highestDueCustomer ? _self.highestDueCustomer : highestDueCustomer // ignore: cast_nullable_to_non_nullable
as DashboardCustomerDueDto?,topFiveDueCustomers: freezed == topFiveDueCustomers ? _self._topFiveDueCustomers : topFiveDueCustomers // ignore: cast_nullable_to_non_nullable
as List<DashboardCustomerDueDto>?,alerts: null == alerts ? _self._alerts : alerts // ignore: cast_nullable_to_non_nullable
as List<DashboardAlertDto>,salesTrendSeries: freezed == salesTrendSeries ? _self._salesTrendSeries : salesTrendSeries // ignore: cast_nullable_to_non_nullable
as List<DashboardSalesTrendPointDto>?,revenueVsExpenses: freezed == revenueVsExpenses ? _self._revenueVsExpenses : revenueVsExpenses // ignore: cast_nullable_to_non_nullable
as List<DashboardRevenueVsExpensesPointDto>?,profitTrendSeries: freezed == profitTrendSeries ? _self._profitTrendSeries : profitTrendSeries // ignore: cast_nullable_to_non_nullable
as List<DashboardProfitTrendPointDto>?,paymentMixTrendSeries: freezed == paymentMixTrendSeries ? _self._paymentMixTrendSeries : paymentMixTrendSeries // ignore: cast_nullable_to_non_nullable
as List<DashboardPaymentMixTrendPointDto>?,previousPeriodSummary: freezed == previousPeriodSummary ? _self.previousPeriodSummary : previousPeriodSummary // ignore: cast_nullable_to_non_nullable
as DashboardPreviousPeriodSummaryDto?,latestSales: null == latestSales ? _self._latestSales : latestSales // ignore: cast_nullable_to_non_nullable
as List<DashboardLatestSaleDto>,stockValue: freezed == stockValue ? _self.stockValue : stockValue // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}

/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$DashboardPaymentMixDtoCopyWith<$Res>? get paymentMix {
    if (_self.paymentMix == null) {
    return null;
  }

  return $DashboardPaymentMixDtoCopyWith<$Res>(_self.paymentMix!, (value) {
    return _then(_self.copyWith(paymentMix: value));
  });
}/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$DashboardCustomerDueDtoCopyWith<$Res>? get highestDueCustomer {
    if (_self.highestDueCustomer == null) {
    return null;
  }

  return $DashboardCustomerDueDtoCopyWith<$Res>(_self.highestDueCustomer!, (value) {
    return _then(_self.copyWith(highestDueCustomer: value));
  });
}/// Create a copy of DashboardDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$DashboardPreviousPeriodSummaryDtoCopyWith<$Res>? get previousPeriodSummary {
    if (_self.previousPeriodSummary == null) {
    return null;
  }

  return $DashboardPreviousPeriodSummaryDtoCopyWith<$Res>(_self.previousPeriodSummary!, (value) {
    return _then(_self.copyWith(previousPeriodSummary: value));
  });
}
}


/// @nodoc
mixin _$DashboardAlertDto {

@JsonKey(name: 'alertType') String get alertType;@JsonKey(name: 'priority') int get priority;@JsonKey(name: 'title') String get title;@JsonKey(name: 'message') String get message;@JsonKey(name: 'actionLabel') String get actionLabel;@JsonKey(name: 'actionRoute') String get actionRoute;
/// Create a copy of DashboardAlertDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardAlertDtoCopyWith<DashboardAlertDto> get copyWith => _$DashboardAlertDtoCopyWithImpl<DashboardAlertDto>(this as DashboardAlertDto, _$identity);

  /// Serializes this DashboardAlertDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardAlertDto&&(identical(other.alertType, alertType) || other.alertType == alertType)&&(identical(other.priority, priority) || other.priority == priority)&&(identical(other.title, title) || other.title == title)&&(identical(other.message, message) || other.message == message)&&(identical(other.actionLabel, actionLabel) || other.actionLabel == actionLabel)&&(identical(other.actionRoute, actionRoute) || other.actionRoute == actionRoute));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,alertType,priority,title,message,actionLabel,actionRoute);

@override
String toString() {
  return 'DashboardAlertDto(alertType: $alertType, priority: $priority, title: $title, message: $message, actionLabel: $actionLabel, actionRoute: $actionRoute)';
}


}

/// @nodoc
abstract mixin class $DashboardAlertDtoCopyWith<$Res>  {
  factory $DashboardAlertDtoCopyWith(DashboardAlertDto value, $Res Function(DashboardAlertDto) _then) = _$DashboardAlertDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'alertType') String alertType,@JsonKey(name: 'priority') int priority,@JsonKey(name: 'title') String title,@JsonKey(name: 'message') String message,@JsonKey(name: 'actionLabel') String actionLabel,@JsonKey(name: 'actionRoute') String actionRoute
});




}
/// @nodoc
class _$DashboardAlertDtoCopyWithImpl<$Res>
    implements $DashboardAlertDtoCopyWith<$Res> {
  _$DashboardAlertDtoCopyWithImpl(this._self, this._then);

  final DashboardAlertDto _self;
  final $Res Function(DashboardAlertDto) _then;

/// Create a copy of DashboardAlertDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? alertType = null,Object? priority = null,Object? title = null,Object? message = null,Object? actionLabel = null,Object? actionRoute = null,}) {
  return _then(_self.copyWith(
alertType: null == alertType ? _self.alertType : alertType // ignore: cast_nullable_to_non_nullable
as String,priority: null == priority ? _self.priority : priority // ignore: cast_nullable_to_non_nullable
as int,title: null == title ? _self.title : title // ignore: cast_nullable_to_non_nullable
as String,message: null == message ? _self.message : message // ignore: cast_nullable_to_non_nullable
as String,actionLabel: null == actionLabel ? _self.actionLabel : actionLabel // ignore: cast_nullable_to_non_nullable
as String,actionRoute: null == actionRoute ? _self.actionRoute : actionRoute // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardAlertDto].
extension DashboardAlertDtoPatterns on DashboardAlertDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardAlertDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardAlertDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardAlertDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardAlertDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardAlertDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardAlertDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'alertType')  String alertType, @JsonKey(name: 'priority')  int priority, @JsonKey(name: 'title')  String title, @JsonKey(name: 'message')  String message, @JsonKey(name: 'actionLabel')  String actionLabel, @JsonKey(name: 'actionRoute')  String actionRoute)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardAlertDto() when $default != null:
return $default(_that.alertType,_that.priority,_that.title,_that.message,_that.actionLabel,_that.actionRoute);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'alertType')  String alertType, @JsonKey(name: 'priority')  int priority, @JsonKey(name: 'title')  String title, @JsonKey(name: 'message')  String message, @JsonKey(name: 'actionLabel')  String actionLabel, @JsonKey(name: 'actionRoute')  String actionRoute)  $default,) {final _that = this;
switch (_that) {
case _DashboardAlertDto():
return $default(_that.alertType,_that.priority,_that.title,_that.message,_that.actionLabel,_that.actionRoute);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'alertType')  String alertType, @JsonKey(name: 'priority')  int priority, @JsonKey(name: 'title')  String title, @JsonKey(name: 'message')  String message, @JsonKey(name: 'actionLabel')  String actionLabel, @JsonKey(name: 'actionRoute')  String actionRoute)?  $default,) {final _that = this;
switch (_that) {
case _DashboardAlertDto() when $default != null:
return $default(_that.alertType,_that.priority,_that.title,_that.message,_that.actionLabel,_that.actionRoute);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardAlertDto implements DashboardAlertDto {
  const _DashboardAlertDto({@JsonKey(name: 'alertType') required this.alertType, @JsonKey(name: 'priority') required this.priority, @JsonKey(name: 'title') required this.title, @JsonKey(name: 'message') required this.message, @JsonKey(name: 'actionLabel') required this.actionLabel, @JsonKey(name: 'actionRoute') required this.actionRoute});
  factory _DashboardAlertDto.fromJson(Map<String, dynamic> json) => _$DashboardAlertDtoFromJson(json);

@override@JsonKey(name: 'alertType') final  String alertType;
@override@JsonKey(name: 'priority') final  int priority;
@override@JsonKey(name: 'title') final  String title;
@override@JsonKey(name: 'message') final  String message;
@override@JsonKey(name: 'actionLabel') final  String actionLabel;
@override@JsonKey(name: 'actionRoute') final  String actionRoute;

/// Create a copy of DashboardAlertDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardAlertDtoCopyWith<_DashboardAlertDto> get copyWith => __$DashboardAlertDtoCopyWithImpl<_DashboardAlertDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardAlertDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardAlertDto&&(identical(other.alertType, alertType) || other.alertType == alertType)&&(identical(other.priority, priority) || other.priority == priority)&&(identical(other.title, title) || other.title == title)&&(identical(other.message, message) || other.message == message)&&(identical(other.actionLabel, actionLabel) || other.actionLabel == actionLabel)&&(identical(other.actionRoute, actionRoute) || other.actionRoute == actionRoute));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,alertType,priority,title,message,actionLabel,actionRoute);

@override
String toString() {
  return 'DashboardAlertDto(alertType: $alertType, priority: $priority, title: $title, message: $message, actionLabel: $actionLabel, actionRoute: $actionRoute)';
}


}

/// @nodoc
abstract mixin class _$DashboardAlertDtoCopyWith<$Res> implements $DashboardAlertDtoCopyWith<$Res> {
  factory _$DashboardAlertDtoCopyWith(_DashboardAlertDto value, $Res Function(_DashboardAlertDto) _then) = __$DashboardAlertDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'alertType') String alertType,@JsonKey(name: 'priority') int priority,@JsonKey(name: 'title') String title,@JsonKey(name: 'message') String message,@JsonKey(name: 'actionLabel') String actionLabel,@JsonKey(name: 'actionRoute') String actionRoute
});




}
/// @nodoc
class __$DashboardAlertDtoCopyWithImpl<$Res>
    implements _$DashboardAlertDtoCopyWith<$Res> {
  __$DashboardAlertDtoCopyWithImpl(this._self, this._then);

  final _DashboardAlertDto _self;
  final $Res Function(_DashboardAlertDto) _then;

/// Create a copy of DashboardAlertDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? alertType = null,Object? priority = null,Object? title = null,Object? message = null,Object? actionLabel = null,Object? actionRoute = null,}) {
  return _then(_DashboardAlertDto(
alertType: null == alertType ? _self.alertType : alertType // ignore: cast_nullable_to_non_nullable
as String,priority: null == priority ? _self.priority : priority // ignore: cast_nullable_to_non_nullable
as int,title: null == title ? _self.title : title // ignore: cast_nullable_to_non_nullable
as String,message: null == message ? _self.message : message // ignore: cast_nullable_to_non_nullable
as String,actionLabel: null == actionLabel ? _self.actionLabel : actionLabel // ignore: cast_nullable_to_non_nullable
as String,actionRoute: null == actionRoute ? _self.actionRoute : actionRoute // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}


/// @nodoc
mixin _$DashboardLatestSaleDto {

@JsonKey(name: 'saleId') String get saleId;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'customerDisplayName') String get customerDisplayName;@JsonKey(name: 'soldAt') String get soldAt;@JsonKey(name: 'totalAmount') double get totalAmount;
/// Create a copy of DashboardLatestSaleDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardLatestSaleDtoCopyWith<DashboardLatestSaleDto> get copyWith => _$DashboardLatestSaleDtoCopyWithImpl<DashboardLatestSaleDto>(this as DashboardLatestSaleDto, _$identity);

  /// Serializes this DashboardLatestSaleDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardLatestSaleDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerDisplayName, customerDisplayName) || other.customerDisplayName == customerDisplayName)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleId,invoiceNumber,customerDisplayName,soldAt,totalAmount);

@override
String toString() {
  return 'DashboardLatestSaleDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerDisplayName: $customerDisplayName, soldAt: $soldAt, totalAmount: $totalAmount)';
}


}

/// @nodoc
abstract mixin class $DashboardLatestSaleDtoCopyWith<$Res>  {
  factory $DashboardLatestSaleDtoCopyWith(DashboardLatestSaleDto value, $Res Function(DashboardLatestSaleDto) _then) = _$DashboardLatestSaleDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerDisplayName') String customerDisplayName,@JsonKey(name: 'soldAt') String soldAt,@JsonKey(name: 'totalAmount') double totalAmount
});




}
/// @nodoc
class _$DashboardLatestSaleDtoCopyWithImpl<$Res>
    implements $DashboardLatestSaleDtoCopyWith<$Res> {
  _$DashboardLatestSaleDtoCopyWithImpl(this._self, this._then);

  final DashboardLatestSaleDto _self;
  final $Res Function(DashboardLatestSaleDto) _then;

/// Create a copy of DashboardLatestSaleDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerDisplayName = null,Object? soldAt = null,Object? totalAmount = null,}) {
  return _then(_self.copyWith(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerDisplayName: null == customerDisplayName ? _self.customerDisplayName : customerDisplayName // ignore: cast_nullable_to_non_nullable
as String,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as String,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardLatestSaleDto].
extension DashboardLatestSaleDtoPatterns on DashboardLatestSaleDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardLatestSaleDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardLatestSaleDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardLatestSaleDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardLatestSaleDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardLatestSaleDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardLatestSaleDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerDisplayName')  String customerDisplayName, @JsonKey(name: 'soldAt')  String soldAt, @JsonKey(name: 'totalAmount')  double totalAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardLatestSaleDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerDisplayName,_that.soldAt,_that.totalAmount);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerDisplayName')  String customerDisplayName, @JsonKey(name: 'soldAt')  String soldAt, @JsonKey(name: 'totalAmount')  double totalAmount)  $default,) {final _that = this;
switch (_that) {
case _DashboardLatestSaleDto():
return $default(_that.saleId,_that.invoiceNumber,_that.customerDisplayName,_that.soldAt,_that.totalAmount);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerDisplayName')  String customerDisplayName, @JsonKey(name: 'soldAt')  String soldAt, @JsonKey(name: 'totalAmount')  double totalAmount)?  $default,) {final _that = this;
switch (_that) {
case _DashboardLatestSaleDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerDisplayName,_that.soldAt,_that.totalAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardLatestSaleDto implements DashboardLatestSaleDto {
  const _DashboardLatestSaleDto({@JsonKey(name: 'saleId') required this.saleId, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'customerDisplayName') required this.customerDisplayName, @JsonKey(name: 'soldAt') required this.soldAt, @JsonKey(name: 'totalAmount') required this.totalAmount});
  factory _DashboardLatestSaleDto.fromJson(Map<String, dynamic> json) => _$DashboardLatestSaleDtoFromJson(json);

@override@JsonKey(name: 'saleId') final  String saleId;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'customerDisplayName') final  String customerDisplayName;
@override@JsonKey(name: 'soldAt') final  String soldAt;
@override@JsonKey(name: 'totalAmount') final  double totalAmount;

/// Create a copy of DashboardLatestSaleDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardLatestSaleDtoCopyWith<_DashboardLatestSaleDto> get copyWith => __$DashboardLatestSaleDtoCopyWithImpl<_DashboardLatestSaleDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardLatestSaleDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardLatestSaleDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerDisplayName, customerDisplayName) || other.customerDisplayName == customerDisplayName)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleId,invoiceNumber,customerDisplayName,soldAt,totalAmount);

@override
String toString() {
  return 'DashboardLatestSaleDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerDisplayName: $customerDisplayName, soldAt: $soldAt, totalAmount: $totalAmount)';
}


}

/// @nodoc
abstract mixin class _$DashboardLatestSaleDtoCopyWith<$Res> implements $DashboardLatestSaleDtoCopyWith<$Res> {
  factory _$DashboardLatestSaleDtoCopyWith(_DashboardLatestSaleDto value, $Res Function(_DashboardLatestSaleDto) _then) = __$DashboardLatestSaleDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerDisplayName') String customerDisplayName,@JsonKey(name: 'soldAt') String soldAt,@JsonKey(name: 'totalAmount') double totalAmount
});




}
/// @nodoc
class __$DashboardLatestSaleDtoCopyWithImpl<$Res>
    implements _$DashboardLatestSaleDtoCopyWith<$Res> {
  __$DashboardLatestSaleDtoCopyWithImpl(this._self, this._then);

  final _DashboardLatestSaleDto _self;
  final $Res Function(_DashboardLatestSaleDto) _then;

/// Create a copy of DashboardLatestSaleDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerDisplayName = null,Object? soldAt = null,Object? totalAmount = null,}) {
  return _then(_DashboardLatestSaleDto(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerDisplayName: null == customerDisplayName ? _self.customerDisplayName : customerDisplayName // ignore: cast_nullable_to_non_nullable
as String,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as String,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardSalesTrendPointDto {

@JsonKey(name: 'date') String get date;@JsonKey(name: 'amount') double get amount;@JsonKey(name: 'netAmount') double get netAmount;
/// Create a copy of DashboardSalesTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardSalesTrendPointDtoCopyWith<DashboardSalesTrendPointDto> get copyWith => _$DashboardSalesTrendPointDtoCopyWithImpl<DashboardSalesTrendPointDto>(this as DashboardSalesTrendPointDto, _$identity);

  /// Serializes this DashboardSalesTrendPointDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardSalesTrendPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.netAmount, netAmount) || other.netAmount == netAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,amount,netAmount);

@override
String toString() {
  return 'DashboardSalesTrendPointDto(date: $date, amount: $amount, netAmount: $netAmount)';
}


}

/// @nodoc
abstract mixin class $DashboardSalesTrendPointDtoCopyWith<$Res>  {
  factory $DashboardSalesTrendPointDtoCopyWith(DashboardSalesTrendPointDto value, $Res Function(DashboardSalesTrendPointDto) _then) = _$DashboardSalesTrendPointDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'amount') double amount,@JsonKey(name: 'netAmount') double netAmount
});




}
/// @nodoc
class _$DashboardSalesTrendPointDtoCopyWithImpl<$Res>
    implements $DashboardSalesTrendPointDtoCopyWith<$Res> {
  _$DashboardSalesTrendPointDtoCopyWithImpl(this._self, this._then);

  final DashboardSalesTrendPointDto _self;
  final $Res Function(DashboardSalesTrendPointDto) _then;

/// Create a copy of DashboardSalesTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? date = null,Object? amount = null,Object? netAmount = null,}) {
  return _then(_self.copyWith(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,netAmount: null == netAmount ? _self.netAmount : netAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardSalesTrendPointDto].
extension DashboardSalesTrendPointDtoPatterns on DashboardSalesTrendPointDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardSalesTrendPointDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardSalesTrendPointDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardSalesTrendPointDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardSalesTrendPointDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardSalesTrendPointDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardSalesTrendPointDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'amount')  double amount, @JsonKey(name: 'netAmount')  double netAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardSalesTrendPointDto() when $default != null:
return $default(_that.date,_that.amount,_that.netAmount);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'amount')  double amount, @JsonKey(name: 'netAmount')  double netAmount)  $default,) {final _that = this;
switch (_that) {
case _DashboardSalesTrendPointDto():
return $default(_that.date,_that.amount,_that.netAmount);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'amount')  double amount, @JsonKey(name: 'netAmount')  double netAmount)?  $default,) {final _that = this;
switch (_that) {
case _DashboardSalesTrendPointDto() when $default != null:
return $default(_that.date,_that.amount,_that.netAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardSalesTrendPointDto implements DashboardSalesTrendPointDto {
  const _DashboardSalesTrendPointDto({@JsonKey(name: 'date') required this.date, @JsonKey(name: 'amount') required this.amount, @JsonKey(name: 'netAmount') required this.netAmount});
  factory _DashboardSalesTrendPointDto.fromJson(Map<String, dynamic> json) => _$DashboardSalesTrendPointDtoFromJson(json);

@override@JsonKey(name: 'date') final  String date;
@override@JsonKey(name: 'amount') final  double amount;
@override@JsonKey(name: 'netAmount') final  double netAmount;

/// Create a copy of DashboardSalesTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardSalesTrendPointDtoCopyWith<_DashboardSalesTrendPointDto> get copyWith => __$DashboardSalesTrendPointDtoCopyWithImpl<_DashboardSalesTrendPointDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardSalesTrendPointDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardSalesTrendPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.netAmount, netAmount) || other.netAmount == netAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,amount,netAmount);

@override
String toString() {
  return 'DashboardSalesTrendPointDto(date: $date, amount: $amount, netAmount: $netAmount)';
}


}

/// @nodoc
abstract mixin class _$DashboardSalesTrendPointDtoCopyWith<$Res> implements $DashboardSalesTrendPointDtoCopyWith<$Res> {
  factory _$DashboardSalesTrendPointDtoCopyWith(_DashboardSalesTrendPointDto value, $Res Function(_DashboardSalesTrendPointDto) _then) = __$DashboardSalesTrendPointDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'amount') double amount,@JsonKey(name: 'netAmount') double netAmount
});




}
/// @nodoc
class __$DashboardSalesTrendPointDtoCopyWithImpl<$Res>
    implements _$DashboardSalesTrendPointDtoCopyWith<$Res> {
  __$DashboardSalesTrendPointDtoCopyWithImpl(this._self, this._then);

  final _DashboardSalesTrendPointDto _self;
  final $Res Function(_DashboardSalesTrendPointDto) _then;

/// Create a copy of DashboardSalesTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? date = null,Object? amount = null,Object? netAmount = null,}) {
  return _then(_DashboardSalesTrendPointDto(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,netAmount: null == netAmount ? _self.netAmount : netAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardRevenueVsExpensesPointDto {

@JsonKey(name: 'date') String get date;@JsonKey(name: 'revenue') double get revenue;@JsonKey(name: 'expenses') double get expenses;
/// Create a copy of DashboardRevenueVsExpensesPointDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardRevenueVsExpensesPointDtoCopyWith<DashboardRevenueVsExpensesPointDto> get copyWith => _$DashboardRevenueVsExpensesPointDtoCopyWithImpl<DashboardRevenueVsExpensesPointDto>(this as DashboardRevenueVsExpensesPointDto, _$identity);

  /// Serializes this DashboardRevenueVsExpensesPointDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardRevenueVsExpensesPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.revenue, revenue) || other.revenue == revenue)&&(identical(other.expenses, expenses) || other.expenses == expenses));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,revenue,expenses);

@override
String toString() {
  return 'DashboardRevenueVsExpensesPointDto(date: $date, revenue: $revenue, expenses: $expenses)';
}


}

/// @nodoc
abstract mixin class $DashboardRevenueVsExpensesPointDtoCopyWith<$Res>  {
  factory $DashboardRevenueVsExpensesPointDtoCopyWith(DashboardRevenueVsExpensesPointDto value, $Res Function(DashboardRevenueVsExpensesPointDto) _then) = _$DashboardRevenueVsExpensesPointDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'revenue') double revenue,@JsonKey(name: 'expenses') double expenses
});




}
/// @nodoc
class _$DashboardRevenueVsExpensesPointDtoCopyWithImpl<$Res>
    implements $DashboardRevenueVsExpensesPointDtoCopyWith<$Res> {
  _$DashboardRevenueVsExpensesPointDtoCopyWithImpl(this._self, this._then);

  final DashboardRevenueVsExpensesPointDto _self;
  final $Res Function(DashboardRevenueVsExpensesPointDto) _then;

/// Create a copy of DashboardRevenueVsExpensesPointDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? date = null,Object? revenue = null,Object? expenses = null,}) {
  return _then(_self.copyWith(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,revenue: null == revenue ? _self.revenue : revenue // ignore: cast_nullable_to_non_nullable
as double,expenses: null == expenses ? _self.expenses : expenses // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardRevenueVsExpensesPointDto].
extension DashboardRevenueVsExpensesPointDtoPatterns on DashboardRevenueVsExpensesPointDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardRevenueVsExpensesPointDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardRevenueVsExpensesPointDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardRevenueVsExpensesPointDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardRevenueVsExpensesPointDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardRevenueVsExpensesPointDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardRevenueVsExpensesPointDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'revenue')  double revenue, @JsonKey(name: 'expenses')  double expenses)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardRevenueVsExpensesPointDto() when $default != null:
return $default(_that.date,_that.revenue,_that.expenses);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'revenue')  double revenue, @JsonKey(name: 'expenses')  double expenses)  $default,) {final _that = this;
switch (_that) {
case _DashboardRevenueVsExpensesPointDto():
return $default(_that.date,_that.revenue,_that.expenses);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'revenue')  double revenue, @JsonKey(name: 'expenses')  double expenses)?  $default,) {final _that = this;
switch (_that) {
case _DashboardRevenueVsExpensesPointDto() when $default != null:
return $default(_that.date,_that.revenue,_that.expenses);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardRevenueVsExpensesPointDto implements DashboardRevenueVsExpensesPointDto {
  const _DashboardRevenueVsExpensesPointDto({@JsonKey(name: 'date') required this.date, @JsonKey(name: 'revenue') required this.revenue, @JsonKey(name: 'expenses') required this.expenses});
  factory _DashboardRevenueVsExpensesPointDto.fromJson(Map<String, dynamic> json) => _$DashboardRevenueVsExpensesPointDtoFromJson(json);

@override@JsonKey(name: 'date') final  String date;
@override@JsonKey(name: 'revenue') final  double revenue;
@override@JsonKey(name: 'expenses') final  double expenses;

/// Create a copy of DashboardRevenueVsExpensesPointDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardRevenueVsExpensesPointDtoCopyWith<_DashboardRevenueVsExpensesPointDto> get copyWith => __$DashboardRevenueVsExpensesPointDtoCopyWithImpl<_DashboardRevenueVsExpensesPointDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardRevenueVsExpensesPointDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardRevenueVsExpensesPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.revenue, revenue) || other.revenue == revenue)&&(identical(other.expenses, expenses) || other.expenses == expenses));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,revenue,expenses);

@override
String toString() {
  return 'DashboardRevenueVsExpensesPointDto(date: $date, revenue: $revenue, expenses: $expenses)';
}


}

/// @nodoc
abstract mixin class _$DashboardRevenueVsExpensesPointDtoCopyWith<$Res> implements $DashboardRevenueVsExpensesPointDtoCopyWith<$Res> {
  factory _$DashboardRevenueVsExpensesPointDtoCopyWith(_DashboardRevenueVsExpensesPointDto value, $Res Function(_DashboardRevenueVsExpensesPointDto) _then) = __$DashboardRevenueVsExpensesPointDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'revenue') double revenue,@JsonKey(name: 'expenses') double expenses
});




}
/// @nodoc
class __$DashboardRevenueVsExpensesPointDtoCopyWithImpl<$Res>
    implements _$DashboardRevenueVsExpensesPointDtoCopyWith<$Res> {
  __$DashboardRevenueVsExpensesPointDtoCopyWithImpl(this._self, this._then);

  final _DashboardRevenueVsExpensesPointDto _self;
  final $Res Function(_DashboardRevenueVsExpensesPointDto) _then;

/// Create a copy of DashboardRevenueVsExpensesPointDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? date = null,Object? revenue = null,Object? expenses = null,}) {
  return _then(_DashboardRevenueVsExpensesPointDto(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,revenue: null == revenue ? _self.revenue : revenue // ignore: cast_nullable_to_non_nullable
as double,expenses: null == expenses ? _self.expenses : expenses // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardProfitTrendPointDto {

@JsonKey(name: 'date') String get date;@JsonKey(name: 'profitBeforeTax') double get profitBeforeTax;@JsonKey(name: 'profitAfterTax') double get profitAfterTax;
/// Create a copy of DashboardProfitTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardProfitTrendPointDtoCopyWith<DashboardProfitTrendPointDto> get copyWith => _$DashboardProfitTrendPointDtoCopyWithImpl<DashboardProfitTrendPointDto>(this as DashboardProfitTrendPointDto, _$identity);

  /// Serializes this DashboardProfitTrendPointDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardProfitTrendPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.profitBeforeTax, profitBeforeTax) || other.profitBeforeTax == profitBeforeTax)&&(identical(other.profitAfterTax, profitAfterTax) || other.profitAfterTax == profitAfterTax));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,profitBeforeTax,profitAfterTax);

@override
String toString() {
  return 'DashboardProfitTrendPointDto(date: $date, profitBeforeTax: $profitBeforeTax, profitAfterTax: $profitAfterTax)';
}


}

/// @nodoc
abstract mixin class $DashboardProfitTrendPointDtoCopyWith<$Res>  {
  factory $DashboardProfitTrendPointDtoCopyWith(DashboardProfitTrendPointDto value, $Res Function(DashboardProfitTrendPointDto) _then) = _$DashboardProfitTrendPointDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'profitBeforeTax') double profitBeforeTax,@JsonKey(name: 'profitAfterTax') double profitAfterTax
});




}
/// @nodoc
class _$DashboardProfitTrendPointDtoCopyWithImpl<$Res>
    implements $DashboardProfitTrendPointDtoCopyWith<$Res> {
  _$DashboardProfitTrendPointDtoCopyWithImpl(this._self, this._then);

  final DashboardProfitTrendPointDto _self;
  final $Res Function(DashboardProfitTrendPointDto) _then;

/// Create a copy of DashboardProfitTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? date = null,Object? profitBeforeTax = null,Object? profitAfterTax = null,}) {
  return _then(_self.copyWith(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,profitBeforeTax: null == profitBeforeTax ? _self.profitBeforeTax : profitBeforeTax // ignore: cast_nullable_to_non_nullable
as double,profitAfterTax: null == profitAfterTax ? _self.profitAfterTax : profitAfterTax // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardProfitTrendPointDto].
extension DashboardProfitTrendPointDtoPatterns on DashboardProfitTrendPointDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardProfitTrendPointDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardProfitTrendPointDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardProfitTrendPointDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardProfitTrendPointDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardProfitTrendPointDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardProfitTrendPointDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'profitBeforeTax')  double profitBeforeTax, @JsonKey(name: 'profitAfterTax')  double profitAfterTax)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardProfitTrendPointDto() when $default != null:
return $default(_that.date,_that.profitBeforeTax,_that.profitAfterTax);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'profitBeforeTax')  double profitBeforeTax, @JsonKey(name: 'profitAfterTax')  double profitAfterTax)  $default,) {final _that = this;
switch (_that) {
case _DashboardProfitTrendPointDto():
return $default(_that.date,_that.profitBeforeTax,_that.profitAfterTax);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'profitBeforeTax')  double profitBeforeTax, @JsonKey(name: 'profitAfterTax')  double profitAfterTax)?  $default,) {final _that = this;
switch (_that) {
case _DashboardProfitTrendPointDto() when $default != null:
return $default(_that.date,_that.profitBeforeTax,_that.profitAfterTax);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardProfitTrendPointDto implements DashboardProfitTrendPointDto {
  const _DashboardProfitTrendPointDto({@JsonKey(name: 'date') required this.date, @JsonKey(name: 'profitBeforeTax') required this.profitBeforeTax, @JsonKey(name: 'profitAfterTax') required this.profitAfterTax});
  factory _DashboardProfitTrendPointDto.fromJson(Map<String, dynamic> json) => _$DashboardProfitTrendPointDtoFromJson(json);

@override@JsonKey(name: 'date') final  String date;
@override@JsonKey(name: 'profitBeforeTax') final  double profitBeforeTax;
@override@JsonKey(name: 'profitAfterTax') final  double profitAfterTax;

/// Create a copy of DashboardProfitTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardProfitTrendPointDtoCopyWith<_DashboardProfitTrendPointDto> get copyWith => __$DashboardProfitTrendPointDtoCopyWithImpl<_DashboardProfitTrendPointDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardProfitTrendPointDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardProfitTrendPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.profitBeforeTax, profitBeforeTax) || other.profitBeforeTax == profitBeforeTax)&&(identical(other.profitAfterTax, profitAfterTax) || other.profitAfterTax == profitAfterTax));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,profitBeforeTax,profitAfterTax);

@override
String toString() {
  return 'DashboardProfitTrendPointDto(date: $date, profitBeforeTax: $profitBeforeTax, profitAfterTax: $profitAfterTax)';
}


}

/// @nodoc
abstract mixin class _$DashboardProfitTrendPointDtoCopyWith<$Res> implements $DashboardProfitTrendPointDtoCopyWith<$Res> {
  factory _$DashboardProfitTrendPointDtoCopyWith(_DashboardProfitTrendPointDto value, $Res Function(_DashboardProfitTrendPointDto) _then) = __$DashboardProfitTrendPointDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'profitBeforeTax') double profitBeforeTax,@JsonKey(name: 'profitAfterTax') double profitAfterTax
});




}
/// @nodoc
class __$DashboardProfitTrendPointDtoCopyWithImpl<$Res>
    implements _$DashboardProfitTrendPointDtoCopyWith<$Res> {
  __$DashboardProfitTrendPointDtoCopyWithImpl(this._self, this._then);

  final _DashboardProfitTrendPointDto _self;
  final $Res Function(_DashboardProfitTrendPointDto) _then;

/// Create a copy of DashboardProfitTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? date = null,Object? profitBeforeTax = null,Object? profitAfterTax = null,}) {
  return _then(_DashboardProfitTrendPointDto(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,profitBeforeTax: null == profitBeforeTax ? _self.profitBeforeTax : profitBeforeTax // ignore: cast_nullable_to_non_nullable
as double,profitAfterTax: null == profitAfterTax ? _self.profitAfterTax : profitAfterTax // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardPaymentMixDto {

@JsonKey(name: 'cash') double get cash;@JsonKey(name: 'upi') double get upi;@JsonKey(name: 'card') double get card;@JsonKey(name: 'credit') double get credit;
/// Create a copy of DashboardPaymentMixDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardPaymentMixDtoCopyWith<DashboardPaymentMixDto> get copyWith => _$DashboardPaymentMixDtoCopyWithImpl<DashboardPaymentMixDto>(this as DashboardPaymentMixDto, _$identity);

  /// Serializes this DashboardPaymentMixDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardPaymentMixDto&&(identical(other.cash, cash) || other.cash == cash)&&(identical(other.upi, upi) || other.upi == upi)&&(identical(other.card, card) || other.card == card)&&(identical(other.credit, credit) || other.credit == credit));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,cash,upi,card,credit);

@override
String toString() {
  return 'DashboardPaymentMixDto(cash: $cash, upi: $upi, card: $card, credit: $credit)';
}


}

/// @nodoc
abstract mixin class $DashboardPaymentMixDtoCopyWith<$Res>  {
  factory $DashboardPaymentMixDtoCopyWith(DashboardPaymentMixDto value, $Res Function(DashboardPaymentMixDto) _then) = _$DashboardPaymentMixDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'cash') double cash,@JsonKey(name: 'upi') double upi,@JsonKey(name: 'card') double card,@JsonKey(name: 'credit') double credit
});




}
/// @nodoc
class _$DashboardPaymentMixDtoCopyWithImpl<$Res>
    implements $DashboardPaymentMixDtoCopyWith<$Res> {
  _$DashboardPaymentMixDtoCopyWithImpl(this._self, this._then);

  final DashboardPaymentMixDto _self;
  final $Res Function(DashboardPaymentMixDto) _then;

/// Create a copy of DashboardPaymentMixDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? cash = null,Object? upi = null,Object? card = null,Object? credit = null,}) {
  return _then(_self.copyWith(
cash: null == cash ? _self.cash : cash // ignore: cast_nullable_to_non_nullable
as double,upi: null == upi ? _self.upi : upi // ignore: cast_nullable_to_non_nullable
as double,card: null == card ? _self.card : card // ignore: cast_nullable_to_non_nullable
as double,credit: null == credit ? _self.credit : credit // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardPaymentMixDto].
extension DashboardPaymentMixDtoPatterns on DashboardPaymentMixDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardPaymentMixDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardPaymentMixDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardPaymentMixDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardPaymentMixDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardPaymentMixDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardPaymentMixDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'cash')  double cash, @JsonKey(name: 'upi')  double upi, @JsonKey(name: 'card')  double card, @JsonKey(name: 'credit')  double credit)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardPaymentMixDto() when $default != null:
return $default(_that.cash,_that.upi,_that.card,_that.credit);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'cash')  double cash, @JsonKey(name: 'upi')  double upi, @JsonKey(name: 'card')  double card, @JsonKey(name: 'credit')  double credit)  $default,) {final _that = this;
switch (_that) {
case _DashboardPaymentMixDto():
return $default(_that.cash,_that.upi,_that.card,_that.credit);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'cash')  double cash, @JsonKey(name: 'upi')  double upi, @JsonKey(name: 'card')  double card, @JsonKey(name: 'credit')  double credit)?  $default,) {final _that = this;
switch (_that) {
case _DashboardPaymentMixDto() when $default != null:
return $default(_that.cash,_that.upi,_that.card,_that.credit);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardPaymentMixDto implements DashboardPaymentMixDto {
  const _DashboardPaymentMixDto({@JsonKey(name: 'cash') required this.cash, @JsonKey(name: 'upi') required this.upi, @JsonKey(name: 'card') required this.card, @JsonKey(name: 'credit') required this.credit});
  factory _DashboardPaymentMixDto.fromJson(Map<String, dynamic> json) => _$DashboardPaymentMixDtoFromJson(json);

@override@JsonKey(name: 'cash') final  double cash;
@override@JsonKey(name: 'upi') final  double upi;
@override@JsonKey(name: 'card') final  double card;
@override@JsonKey(name: 'credit') final  double credit;

/// Create a copy of DashboardPaymentMixDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardPaymentMixDtoCopyWith<_DashboardPaymentMixDto> get copyWith => __$DashboardPaymentMixDtoCopyWithImpl<_DashboardPaymentMixDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardPaymentMixDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardPaymentMixDto&&(identical(other.cash, cash) || other.cash == cash)&&(identical(other.upi, upi) || other.upi == upi)&&(identical(other.card, card) || other.card == card)&&(identical(other.credit, credit) || other.credit == credit));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,cash,upi,card,credit);

@override
String toString() {
  return 'DashboardPaymentMixDto(cash: $cash, upi: $upi, card: $card, credit: $credit)';
}


}

/// @nodoc
abstract mixin class _$DashboardPaymentMixDtoCopyWith<$Res> implements $DashboardPaymentMixDtoCopyWith<$Res> {
  factory _$DashboardPaymentMixDtoCopyWith(_DashboardPaymentMixDto value, $Res Function(_DashboardPaymentMixDto) _then) = __$DashboardPaymentMixDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'cash') double cash,@JsonKey(name: 'upi') double upi,@JsonKey(name: 'card') double card,@JsonKey(name: 'credit') double credit
});




}
/// @nodoc
class __$DashboardPaymentMixDtoCopyWithImpl<$Res>
    implements _$DashboardPaymentMixDtoCopyWith<$Res> {
  __$DashboardPaymentMixDtoCopyWithImpl(this._self, this._then);

  final _DashboardPaymentMixDto _self;
  final $Res Function(_DashboardPaymentMixDto) _then;

/// Create a copy of DashboardPaymentMixDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? cash = null,Object? upi = null,Object? card = null,Object? credit = null,}) {
  return _then(_DashboardPaymentMixDto(
cash: null == cash ? _self.cash : cash // ignore: cast_nullable_to_non_nullable
as double,upi: null == upi ? _self.upi : upi // ignore: cast_nullable_to_non_nullable
as double,card: null == card ? _self.card : card // ignore: cast_nullable_to_non_nullable
as double,credit: null == credit ? _self.credit : credit // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardPaymentMixTrendPointDto {

@JsonKey(name: 'date') String get date;@JsonKey(name: 'cash') double get cash;@JsonKey(name: 'upi') double get upi;@JsonKey(name: 'card') double get card;@JsonKey(name: 'credit') double get credit;
/// Create a copy of DashboardPaymentMixTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardPaymentMixTrendPointDtoCopyWith<DashboardPaymentMixTrendPointDto> get copyWith => _$DashboardPaymentMixTrendPointDtoCopyWithImpl<DashboardPaymentMixTrendPointDto>(this as DashboardPaymentMixTrendPointDto, _$identity);

  /// Serializes this DashboardPaymentMixTrendPointDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardPaymentMixTrendPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.cash, cash) || other.cash == cash)&&(identical(other.upi, upi) || other.upi == upi)&&(identical(other.card, card) || other.card == card)&&(identical(other.credit, credit) || other.credit == credit));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,cash,upi,card,credit);

@override
String toString() {
  return 'DashboardPaymentMixTrendPointDto(date: $date, cash: $cash, upi: $upi, card: $card, credit: $credit)';
}


}

/// @nodoc
abstract mixin class $DashboardPaymentMixTrendPointDtoCopyWith<$Res>  {
  factory $DashboardPaymentMixTrendPointDtoCopyWith(DashboardPaymentMixTrendPointDto value, $Res Function(DashboardPaymentMixTrendPointDto) _then) = _$DashboardPaymentMixTrendPointDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'cash') double cash,@JsonKey(name: 'upi') double upi,@JsonKey(name: 'card') double card,@JsonKey(name: 'credit') double credit
});




}
/// @nodoc
class _$DashboardPaymentMixTrendPointDtoCopyWithImpl<$Res>
    implements $DashboardPaymentMixTrendPointDtoCopyWith<$Res> {
  _$DashboardPaymentMixTrendPointDtoCopyWithImpl(this._self, this._then);

  final DashboardPaymentMixTrendPointDto _self;
  final $Res Function(DashboardPaymentMixTrendPointDto) _then;

/// Create a copy of DashboardPaymentMixTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? date = null,Object? cash = null,Object? upi = null,Object? card = null,Object? credit = null,}) {
  return _then(_self.copyWith(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,cash: null == cash ? _self.cash : cash // ignore: cast_nullable_to_non_nullable
as double,upi: null == upi ? _self.upi : upi // ignore: cast_nullable_to_non_nullable
as double,card: null == card ? _self.card : card // ignore: cast_nullable_to_non_nullable
as double,credit: null == credit ? _self.credit : credit // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardPaymentMixTrendPointDto].
extension DashboardPaymentMixTrendPointDtoPatterns on DashboardPaymentMixTrendPointDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardPaymentMixTrendPointDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardPaymentMixTrendPointDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardPaymentMixTrendPointDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardPaymentMixTrendPointDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardPaymentMixTrendPointDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardPaymentMixTrendPointDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'cash')  double cash, @JsonKey(name: 'upi')  double upi, @JsonKey(name: 'card')  double card, @JsonKey(name: 'credit')  double credit)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardPaymentMixTrendPointDto() when $default != null:
return $default(_that.date,_that.cash,_that.upi,_that.card,_that.credit);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'cash')  double cash, @JsonKey(name: 'upi')  double upi, @JsonKey(name: 'card')  double card, @JsonKey(name: 'credit')  double credit)  $default,) {final _that = this;
switch (_that) {
case _DashboardPaymentMixTrendPointDto():
return $default(_that.date,_that.cash,_that.upi,_that.card,_that.credit);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'date')  String date, @JsonKey(name: 'cash')  double cash, @JsonKey(name: 'upi')  double upi, @JsonKey(name: 'card')  double card, @JsonKey(name: 'credit')  double credit)?  $default,) {final _that = this;
switch (_that) {
case _DashboardPaymentMixTrendPointDto() when $default != null:
return $default(_that.date,_that.cash,_that.upi,_that.card,_that.credit);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardPaymentMixTrendPointDto implements DashboardPaymentMixTrendPointDto {
  const _DashboardPaymentMixTrendPointDto({@JsonKey(name: 'date') required this.date, @JsonKey(name: 'cash') required this.cash, @JsonKey(name: 'upi') required this.upi, @JsonKey(name: 'card') required this.card, @JsonKey(name: 'credit') required this.credit});
  factory _DashboardPaymentMixTrendPointDto.fromJson(Map<String, dynamic> json) => _$DashboardPaymentMixTrendPointDtoFromJson(json);

@override@JsonKey(name: 'date') final  String date;
@override@JsonKey(name: 'cash') final  double cash;
@override@JsonKey(name: 'upi') final  double upi;
@override@JsonKey(name: 'card') final  double card;
@override@JsonKey(name: 'credit') final  double credit;

/// Create a copy of DashboardPaymentMixTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardPaymentMixTrendPointDtoCopyWith<_DashboardPaymentMixTrendPointDto> get copyWith => __$DashboardPaymentMixTrendPointDtoCopyWithImpl<_DashboardPaymentMixTrendPointDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardPaymentMixTrendPointDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardPaymentMixTrendPointDto&&(identical(other.date, date) || other.date == date)&&(identical(other.cash, cash) || other.cash == cash)&&(identical(other.upi, upi) || other.upi == upi)&&(identical(other.card, card) || other.card == card)&&(identical(other.credit, credit) || other.credit == credit));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,date,cash,upi,card,credit);

@override
String toString() {
  return 'DashboardPaymentMixTrendPointDto(date: $date, cash: $cash, upi: $upi, card: $card, credit: $credit)';
}


}

/// @nodoc
abstract mixin class _$DashboardPaymentMixTrendPointDtoCopyWith<$Res> implements $DashboardPaymentMixTrendPointDtoCopyWith<$Res> {
  factory _$DashboardPaymentMixTrendPointDtoCopyWith(_DashboardPaymentMixTrendPointDto value, $Res Function(_DashboardPaymentMixTrendPointDto) _then) = __$DashboardPaymentMixTrendPointDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'date') String date,@JsonKey(name: 'cash') double cash,@JsonKey(name: 'upi') double upi,@JsonKey(name: 'card') double card,@JsonKey(name: 'credit') double credit
});




}
/// @nodoc
class __$DashboardPaymentMixTrendPointDtoCopyWithImpl<$Res>
    implements _$DashboardPaymentMixTrendPointDtoCopyWith<$Res> {
  __$DashboardPaymentMixTrendPointDtoCopyWithImpl(this._self, this._then);

  final _DashboardPaymentMixTrendPointDto _self;
  final $Res Function(_DashboardPaymentMixTrendPointDto) _then;

/// Create a copy of DashboardPaymentMixTrendPointDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? date = null,Object? cash = null,Object? upi = null,Object? card = null,Object? credit = null,}) {
  return _then(_DashboardPaymentMixTrendPointDto(
date: null == date ? _self.date : date // ignore: cast_nullable_to_non_nullable
as String,cash: null == cash ? _self.cash : cash // ignore: cast_nullable_to_non_nullable
as double,upi: null == upi ? _self.upi : upi // ignore: cast_nullable_to_non_nullable
as double,card: null == card ? _self.card : card // ignore: cast_nullable_to_non_nullable
as double,credit: null == credit ? _self.credit : credit // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardPreviousPeriodSummaryDto {

@JsonKey(name: 'startDate') String get startDate;@JsonKey(name: 'endDate') String get endDate;@JsonKey(name: 'salesCount') int get salesCount;@JsonKey(name: 'salesBooked') double get salesBooked;@JsonKey(name: 'netSalesBooked') double get netSalesBooked;@JsonKey(name: 'profitAfterTax') double get profitAfterTax;@JsonKey(name: 'netExpense') double get netExpense;@JsonKey(name: 'creditSalesPercentage') double get creditSalesPercentage;
/// Create a copy of DashboardPreviousPeriodSummaryDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardPreviousPeriodSummaryDtoCopyWith<DashboardPreviousPeriodSummaryDto> get copyWith => _$DashboardPreviousPeriodSummaryDtoCopyWithImpl<DashboardPreviousPeriodSummaryDto>(this as DashboardPreviousPeriodSummaryDto, _$identity);

  /// Serializes this DashboardPreviousPeriodSummaryDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardPreviousPeriodSummaryDto&&(identical(other.startDate, startDate) || other.startDate == startDate)&&(identical(other.endDate, endDate) || other.endDate == endDate)&&(identical(other.salesCount, salesCount) || other.salesCount == salesCount)&&(identical(other.salesBooked, salesBooked) || other.salesBooked == salesBooked)&&(identical(other.netSalesBooked, netSalesBooked) || other.netSalesBooked == netSalesBooked)&&(identical(other.profitAfterTax, profitAfterTax) || other.profitAfterTax == profitAfterTax)&&(identical(other.netExpense, netExpense) || other.netExpense == netExpense)&&(identical(other.creditSalesPercentage, creditSalesPercentage) || other.creditSalesPercentage == creditSalesPercentage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,startDate,endDate,salesCount,salesBooked,netSalesBooked,profitAfterTax,netExpense,creditSalesPercentage);

@override
String toString() {
  return 'DashboardPreviousPeriodSummaryDto(startDate: $startDate, endDate: $endDate, salesCount: $salesCount, salesBooked: $salesBooked, netSalesBooked: $netSalesBooked, profitAfterTax: $profitAfterTax, netExpense: $netExpense, creditSalesPercentage: $creditSalesPercentage)';
}


}

/// @nodoc
abstract mixin class $DashboardPreviousPeriodSummaryDtoCopyWith<$Res>  {
  factory $DashboardPreviousPeriodSummaryDtoCopyWith(DashboardPreviousPeriodSummaryDto value, $Res Function(DashboardPreviousPeriodSummaryDto) _then) = _$DashboardPreviousPeriodSummaryDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'startDate') String startDate,@JsonKey(name: 'endDate') String endDate,@JsonKey(name: 'salesCount') int salesCount,@JsonKey(name: 'salesBooked') double salesBooked,@JsonKey(name: 'netSalesBooked') double netSalesBooked,@JsonKey(name: 'profitAfterTax') double profitAfterTax,@JsonKey(name: 'netExpense') double netExpense,@JsonKey(name: 'creditSalesPercentage') double creditSalesPercentage
});




}
/// @nodoc
class _$DashboardPreviousPeriodSummaryDtoCopyWithImpl<$Res>
    implements $DashboardPreviousPeriodSummaryDtoCopyWith<$Res> {
  _$DashboardPreviousPeriodSummaryDtoCopyWithImpl(this._self, this._then);

  final DashboardPreviousPeriodSummaryDto _self;
  final $Res Function(DashboardPreviousPeriodSummaryDto) _then;

/// Create a copy of DashboardPreviousPeriodSummaryDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? startDate = null,Object? endDate = null,Object? salesCount = null,Object? salesBooked = null,Object? netSalesBooked = null,Object? profitAfterTax = null,Object? netExpense = null,Object? creditSalesPercentage = null,}) {
  return _then(_self.copyWith(
startDate: null == startDate ? _self.startDate : startDate // ignore: cast_nullable_to_non_nullable
as String,endDate: null == endDate ? _self.endDate : endDate // ignore: cast_nullable_to_non_nullable
as String,salesCount: null == salesCount ? _self.salesCount : salesCount // ignore: cast_nullable_to_non_nullable
as int,salesBooked: null == salesBooked ? _self.salesBooked : salesBooked // ignore: cast_nullable_to_non_nullable
as double,netSalesBooked: null == netSalesBooked ? _self.netSalesBooked : netSalesBooked // ignore: cast_nullable_to_non_nullable
as double,profitAfterTax: null == profitAfterTax ? _self.profitAfterTax : profitAfterTax // ignore: cast_nullable_to_non_nullable
as double,netExpense: null == netExpense ? _self.netExpense : netExpense // ignore: cast_nullable_to_non_nullable
as double,creditSalesPercentage: null == creditSalesPercentage ? _self.creditSalesPercentage : creditSalesPercentage // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardPreviousPeriodSummaryDto].
extension DashboardPreviousPeriodSummaryDtoPatterns on DashboardPreviousPeriodSummaryDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardPreviousPeriodSummaryDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardPreviousPeriodSummaryDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardPreviousPeriodSummaryDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardPreviousPeriodSummaryDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardPreviousPeriodSummaryDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardPreviousPeriodSummaryDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'startDate')  String startDate, @JsonKey(name: 'endDate')  String endDate, @JsonKey(name: 'salesCount')  int salesCount, @JsonKey(name: 'salesBooked')  double salesBooked, @JsonKey(name: 'netSalesBooked')  double netSalesBooked, @JsonKey(name: 'profitAfterTax')  double profitAfterTax, @JsonKey(name: 'netExpense')  double netExpense, @JsonKey(name: 'creditSalesPercentage')  double creditSalesPercentage)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardPreviousPeriodSummaryDto() when $default != null:
return $default(_that.startDate,_that.endDate,_that.salesCount,_that.salesBooked,_that.netSalesBooked,_that.profitAfterTax,_that.netExpense,_that.creditSalesPercentage);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'startDate')  String startDate, @JsonKey(name: 'endDate')  String endDate, @JsonKey(name: 'salesCount')  int salesCount, @JsonKey(name: 'salesBooked')  double salesBooked, @JsonKey(name: 'netSalesBooked')  double netSalesBooked, @JsonKey(name: 'profitAfterTax')  double profitAfterTax, @JsonKey(name: 'netExpense')  double netExpense, @JsonKey(name: 'creditSalesPercentage')  double creditSalesPercentage)  $default,) {final _that = this;
switch (_that) {
case _DashboardPreviousPeriodSummaryDto():
return $default(_that.startDate,_that.endDate,_that.salesCount,_that.salesBooked,_that.netSalesBooked,_that.profitAfterTax,_that.netExpense,_that.creditSalesPercentage);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'startDate')  String startDate, @JsonKey(name: 'endDate')  String endDate, @JsonKey(name: 'salesCount')  int salesCount, @JsonKey(name: 'salesBooked')  double salesBooked, @JsonKey(name: 'netSalesBooked')  double netSalesBooked, @JsonKey(name: 'profitAfterTax')  double profitAfterTax, @JsonKey(name: 'netExpense')  double netExpense, @JsonKey(name: 'creditSalesPercentage')  double creditSalesPercentage)?  $default,) {final _that = this;
switch (_that) {
case _DashboardPreviousPeriodSummaryDto() when $default != null:
return $default(_that.startDate,_that.endDate,_that.salesCount,_that.salesBooked,_that.netSalesBooked,_that.profitAfterTax,_that.netExpense,_that.creditSalesPercentage);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardPreviousPeriodSummaryDto implements DashboardPreviousPeriodSummaryDto {
  const _DashboardPreviousPeriodSummaryDto({@JsonKey(name: 'startDate') required this.startDate, @JsonKey(name: 'endDate') required this.endDate, @JsonKey(name: 'salesCount') required this.salesCount, @JsonKey(name: 'salesBooked') required this.salesBooked, @JsonKey(name: 'netSalesBooked') required this.netSalesBooked, @JsonKey(name: 'profitAfterTax') required this.profitAfterTax, @JsonKey(name: 'netExpense') required this.netExpense, @JsonKey(name: 'creditSalesPercentage') required this.creditSalesPercentage});
  factory _DashboardPreviousPeriodSummaryDto.fromJson(Map<String, dynamic> json) => _$DashboardPreviousPeriodSummaryDtoFromJson(json);

@override@JsonKey(name: 'startDate') final  String startDate;
@override@JsonKey(name: 'endDate') final  String endDate;
@override@JsonKey(name: 'salesCount') final  int salesCount;
@override@JsonKey(name: 'salesBooked') final  double salesBooked;
@override@JsonKey(name: 'netSalesBooked') final  double netSalesBooked;
@override@JsonKey(name: 'profitAfterTax') final  double profitAfterTax;
@override@JsonKey(name: 'netExpense') final  double netExpense;
@override@JsonKey(name: 'creditSalesPercentage') final  double creditSalesPercentage;

/// Create a copy of DashboardPreviousPeriodSummaryDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardPreviousPeriodSummaryDtoCopyWith<_DashboardPreviousPeriodSummaryDto> get copyWith => __$DashboardPreviousPeriodSummaryDtoCopyWithImpl<_DashboardPreviousPeriodSummaryDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardPreviousPeriodSummaryDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardPreviousPeriodSummaryDto&&(identical(other.startDate, startDate) || other.startDate == startDate)&&(identical(other.endDate, endDate) || other.endDate == endDate)&&(identical(other.salesCount, salesCount) || other.salesCount == salesCount)&&(identical(other.salesBooked, salesBooked) || other.salesBooked == salesBooked)&&(identical(other.netSalesBooked, netSalesBooked) || other.netSalesBooked == netSalesBooked)&&(identical(other.profitAfterTax, profitAfterTax) || other.profitAfterTax == profitAfterTax)&&(identical(other.netExpense, netExpense) || other.netExpense == netExpense)&&(identical(other.creditSalesPercentage, creditSalesPercentage) || other.creditSalesPercentage == creditSalesPercentage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,startDate,endDate,salesCount,salesBooked,netSalesBooked,profitAfterTax,netExpense,creditSalesPercentage);

@override
String toString() {
  return 'DashboardPreviousPeriodSummaryDto(startDate: $startDate, endDate: $endDate, salesCount: $salesCount, salesBooked: $salesBooked, netSalesBooked: $netSalesBooked, profitAfterTax: $profitAfterTax, netExpense: $netExpense, creditSalesPercentage: $creditSalesPercentage)';
}


}

/// @nodoc
abstract mixin class _$DashboardPreviousPeriodSummaryDtoCopyWith<$Res> implements $DashboardPreviousPeriodSummaryDtoCopyWith<$Res> {
  factory _$DashboardPreviousPeriodSummaryDtoCopyWith(_DashboardPreviousPeriodSummaryDto value, $Res Function(_DashboardPreviousPeriodSummaryDto) _then) = __$DashboardPreviousPeriodSummaryDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'startDate') String startDate,@JsonKey(name: 'endDate') String endDate,@JsonKey(name: 'salesCount') int salesCount,@JsonKey(name: 'salesBooked') double salesBooked,@JsonKey(name: 'netSalesBooked') double netSalesBooked,@JsonKey(name: 'profitAfterTax') double profitAfterTax,@JsonKey(name: 'netExpense') double netExpense,@JsonKey(name: 'creditSalesPercentage') double creditSalesPercentage
});




}
/// @nodoc
class __$DashboardPreviousPeriodSummaryDtoCopyWithImpl<$Res>
    implements _$DashboardPreviousPeriodSummaryDtoCopyWith<$Res> {
  __$DashboardPreviousPeriodSummaryDtoCopyWithImpl(this._self, this._then);

  final _DashboardPreviousPeriodSummaryDto _self;
  final $Res Function(_DashboardPreviousPeriodSummaryDto) _then;

/// Create a copy of DashboardPreviousPeriodSummaryDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? startDate = null,Object? endDate = null,Object? salesCount = null,Object? salesBooked = null,Object? netSalesBooked = null,Object? profitAfterTax = null,Object? netExpense = null,Object? creditSalesPercentage = null,}) {
  return _then(_DashboardPreviousPeriodSummaryDto(
startDate: null == startDate ? _self.startDate : startDate // ignore: cast_nullable_to_non_nullable
as String,endDate: null == endDate ? _self.endDate : endDate // ignore: cast_nullable_to_non_nullable
as String,salesCount: null == salesCount ? _self.salesCount : salesCount // ignore: cast_nullable_to_non_nullable
as int,salesBooked: null == salesBooked ? _self.salesBooked : salesBooked // ignore: cast_nullable_to_non_nullable
as double,netSalesBooked: null == netSalesBooked ? _self.netSalesBooked : netSalesBooked // ignore: cast_nullable_to_non_nullable
as double,profitAfterTax: null == profitAfterTax ? _self.profitAfterTax : profitAfterTax // ignore: cast_nullable_to_non_nullable
as double,netExpense: null == netExpense ? _self.netExpense : netExpense // ignore: cast_nullable_to_non_nullable
as double,creditSalesPercentage: null == creditSalesPercentage ? _self.creditSalesPercentage : creditSalesPercentage // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardStockShortageDto {

@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'reorderLevel') double get reorderLevel;@JsonKey(name: 'shortage') double get shortage;
/// Create a copy of DashboardStockShortageDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardStockShortageDtoCopyWith<DashboardStockShortageDto> get copyWith => _$DashboardStockShortageDtoCopyWithImpl<DashboardStockShortageDto>(this as DashboardStockShortageDto, _$identity);

  /// Serializes this DashboardStockShortageDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardStockShortageDto&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.reorderLevel, reorderLevel) || other.reorderLevel == reorderLevel)&&(identical(other.shortage, shortage) || other.shortage == shortage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemName,quantity,reorderLevel,shortage);

@override
String toString() {
  return 'DashboardStockShortageDto(itemName: $itemName, quantity: $quantity, reorderLevel: $reorderLevel, shortage: $shortage)';
}


}

/// @nodoc
abstract mixin class $DashboardStockShortageDtoCopyWith<$Res>  {
  factory $DashboardStockShortageDtoCopyWith(DashboardStockShortageDto value, $Res Function(DashboardStockShortageDto) _then) = _$DashboardStockShortageDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'reorderLevel') double reorderLevel,@JsonKey(name: 'shortage') double shortage
});




}
/// @nodoc
class _$DashboardStockShortageDtoCopyWithImpl<$Res>
    implements $DashboardStockShortageDtoCopyWith<$Res> {
  _$DashboardStockShortageDtoCopyWithImpl(this._self, this._then);

  final DashboardStockShortageDto _self;
  final $Res Function(DashboardStockShortageDto) _then;

/// Create a copy of DashboardStockShortageDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? itemName = null,Object? quantity = null,Object? reorderLevel = null,Object? shortage = null,}) {
  return _then(_self.copyWith(
itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,reorderLevel: null == reorderLevel ? _self.reorderLevel : reorderLevel // ignore: cast_nullable_to_non_nullable
as double,shortage: null == shortage ? _self.shortage : shortage // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardStockShortageDto].
extension DashboardStockShortageDtoPatterns on DashboardStockShortageDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardStockShortageDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardStockShortageDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardStockShortageDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardStockShortageDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardStockShortageDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardStockShortageDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'reorderLevel')  double reorderLevel, @JsonKey(name: 'shortage')  double shortage)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardStockShortageDto() when $default != null:
return $default(_that.itemName,_that.quantity,_that.reorderLevel,_that.shortage);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'reorderLevel')  double reorderLevel, @JsonKey(name: 'shortage')  double shortage)  $default,) {final _that = this;
switch (_that) {
case _DashboardStockShortageDto():
return $default(_that.itemName,_that.quantity,_that.reorderLevel,_that.shortage);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'reorderLevel')  double reorderLevel, @JsonKey(name: 'shortage')  double shortage)?  $default,) {final _that = this;
switch (_that) {
case _DashboardStockShortageDto() when $default != null:
return $default(_that.itemName,_that.quantity,_that.reorderLevel,_that.shortage);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardStockShortageDto implements DashboardStockShortageDto {
  const _DashboardStockShortageDto({@JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'reorderLevel') required this.reorderLevel, @JsonKey(name: 'shortage') required this.shortage});
  factory _DashboardStockShortageDto.fromJson(Map<String, dynamic> json) => _$DashboardStockShortageDtoFromJson(json);

@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'reorderLevel') final  double reorderLevel;
@override@JsonKey(name: 'shortage') final  double shortage;

/// Create a copy of DashboardStockShortageDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardStockShortageDtoCopyWith<_DashboardStockShortageDto> get copyWith => __$DashboardStockShortageDtoCopyWithImpl<_DashboardStockShortageDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardStockShortageDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardStockShortageDto&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.reorderLevel, reorderLevel) || other.reorderLevel == reorderLevel)&&(identical(other.shortage, shortage) || other.shortage == shortage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemName,quantity,reorderLevel,shortage);

@override
String toString() {
  return 'DashboardStockShortageDto(itemName: $itemName, quantity: $quantity, reorderLevel: $reorderLevel, shortage: $shortage)';
}


}

/// @nodoc
abstract mixin class _$DashboardStockShortageDtoCopyWith<$Res> implements $DashboardStockShortageDtoCopyWith<$Res> {
  factory _$DashboardStockShortageDtoCopyWith(_DashboardStockShortageDto value, $Res Function(_DashboardStockShortageDto) _then) = __$DashboardStockShortageDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'reorderLevel') double reorderLevel,@JsonKey(name: 'shortage') double shortage
});




}
/// @nodoc
class __$DashboardStockShortageDtoCopyWithImpl<$Res>
    implements _$DashboardStockShortageDtoCopyWith<$Res> {
  __$DashboardStockShortageDtoCopyWithImpl(this._self, this._then);

  final _DashboardStockShortageDto _self;
  final $Res Function(_DashboardStockShortageDto) _then;

/// Create a copy of DashboardStockShortageDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? itemName = null,Object? quantity = null,Object? reorderLevel = null,Object? shortage = null,}) {
  return _then(_DashboardStockShortageDto(
itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,reorderLevel: null == reorderLevel ? _self.reorderLevel : reorderLevel // ignore: cast_nullable_to_non_nullable
as double,shortage: null == shortage ? _self.shortage : shortage // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$DashboardCustomerDueDto {

@JsonKey(name: 'customerId') String get customerId;@JsonKey(name: 'displayName') String get displayName;@JsonKey(name: 'outstandingDue') double get outstandingDue;
/// Create a copy of DashboardCustomerDueDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DashboardCustomerDueDtoCopyWith<DashboardCustomerDueDto> get copyWith => _$DashboardCustomerDueDtoCopyWithImpl<DashboardCustomerDueDto>(this as DashboardCustomerDueDto, _$identity);

  /// Serializes this DashboardCustomerDueDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DashboardCustomerDueDto&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.displayName, displayName) || other.displayName == displayName)&&(identical(other.outstandingDue, outstandingDue) || other.outstandingDue == outstandingDue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,customerId,displayName,outstandingDue);

@override
String toString() {
  return 'DashboardCustomerDueDto(customerId: $customerId, displayName: $displayName, outstandingDue: $outstandingDue)';
}


}

/// @nodoc
abstract mixin class $DashboardCustomerDueDtoCopyWith<$Res>  {
  factory $DashboardCustomerDueDtoCopyWith(DashboardCustomerDueDto value, $Res Function(DashboardCustomerDueDto) _then) = _$DashboardCustomerDueDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'customerId') String customerId,@JsonKey(name: 'displayName') String displayName,@JsonKey(name: 'outstandingDue') double outstandingDue
});




}
/// @nodoc
class _$DashboardCustomerDueDtoCopyWithImpl<$Res>
    implements $DashboardCustomerDueDtoCopyWith<$Res> {
  _$DashboardCustomerDueDtoCopyWithImpl(this._self, this._then);

  final DashboardCustomerDueDto _self;
  final $Res Function(DashboardCustomerDueDto) _then;

/// Create a copy of DashboardCustomerDueDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? customerId = null,Object? displayName = null,Object? outstandingDue = null,}) {
  return _then(_self.copyWith(
customerId: null == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String,displayName: null == displayName ? _self.displayName : displayName // ignore: cast_nullable_to_non_nullable
as String,outstandingDue: null == outstandingDue ? _self.outstandingDue : outstandingDue // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DashboardCustomerDueDto].
extension DashboardCustomerDueDtoPatterns on DashboardCustomerDueDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DashboardCustomerDueDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DashboardCustomerDueDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DashboardCustomerDueDto value)  $default,){
final _that = this;
switch (_that) {
case _DashboardCustomerDueDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DashboardCustomerDueDto value)?  $default,){
final _that = this;
switch (_that) {
case _DashboardCustomerDueDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'customerId')  String customerId, @JsonKey(name: 'displayName')  String displayName, @JsonKey(name: 'outstandingDue')  double outstandingDue)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DashboardCustomerDueDto() when $default != null:
return $default(_that.customerId,_that.displayName,_that.outstandingDue);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'customerId')  String customerId, @JsonKey(name: 'displayName')  String displayName, @JsonKey(name: 'outstandingDue')  double outstandingDue)  $default,) {final _that = this;
switch (_that) {
case _DashboardCustomerDueDto():
return $default(_that.customerId,_that.displayName,_that.outstandingDue);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'customerId')  String customerId, @JsonKey(name: 'displayName')  String displayName, @JsonKey(name: 'outstandingDue')  double outstandingDue)?  $default,) {final _that = this;
switch (_that) {
case _DashboardCustomerDueDto() when $default != null:
return $default(_that.customerId,_that.displayName,_that.outstandingDue);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DashboardCustomerDueDto implements DashboardCustomerDueDto {
  const _DashboardCustomerDueDto({@JsonKey(name: 'customerId') required this.customerId, @JsonKey(name: 'displayName') required this.displayName, @JsonKey(name: 'outstandingDue') required this.outstandingDue});
  factory _DashboardCustomerDueDto.fromJson(Map<String, dynamic> json) => _$DashboardCustomerDueDtoFromJson(json);

@override@JsonKey(name: 'customerId') final  String customerId;
@override@JsonKey(name: 'displayName') final  String displayName;
@override@JsonKey(name: 'outstandingDue') final  double outstandingDue;

/// Create a copy of DashboardCustomerDueDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DashboardCustomerDueDtoCopyWith<_DashboardCustomerDueDto> get copyWith => __$DashboardCustomerDueDtoCopyWithImpl<_DashboardCustomerDueDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DashboardCustomerDueDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DashboardCustomerDueDto&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.displayName, displayName) || other.displayName == displayName)&&(identical(other.outstandingDue, outstandingDue) || other.outstandingDue == outstandingDue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,customerId,displayName,outstandingDue);

@override
String toString() {
  return 'DashboardCustomerDueDto(customerId: $customerId, displayName: $displayName, outstandingDue: $outstandingDue)';
}


}

/// @nodoc
abstract mixin class _$DashboardCustomerDueDtoCopyWith<$Res> implements $DashboardCustomerDueDtoCopyWith<$Res> {
  factory _$DashboardCustomerDueDtoCopyWith(_DashboardCustomerDueDto value, $Res Function(_DashboardCustomerDueDto) _then) = __$DashboardCustomerDueDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'customerId') String customerId,@JsonKey(name: 'displayName') String displayName,@JsonKey(name: 'outstandingDue') double outstandingDue
});




}
/// @nodoc
class __$DashboardCustomerDueDtoCopyWithImpl<$Res>
    implements _$DashboardCustomerDueDtoCopyWith<$Res> {
  __$DashboardCustomerDueDtoCopyWithImpl(this._self, this._then);

  final _DashboardCustomerDueDto _self;
  final $Res Function(_DashboardCustomerDueDto) _then;

/// Create a copy of DashboardCustomerDueDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? customerId = null,Object? displayName = null,Object? outstandingDue = null,}) {
  return _then(_DashboardCustomerDueDto(
customerId: null == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String,displayName: null == displayName ? _self.displayName : displayName // ignore: cast_nullable_to_non_nullable
as String,outstandingDue: null == outstandingDue ? _self.outstandingDue : outstandingDue // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
