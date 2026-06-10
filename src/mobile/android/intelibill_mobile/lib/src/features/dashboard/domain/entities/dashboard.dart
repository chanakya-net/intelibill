import 'package:equatable/equatable.dart';

class Dashboard extends Equatable {
  const Dashboard({
    required this.generatedAt,
    required this.startDate,
    required this.endDate,
    required this.salesCount,
    required this.salesRevenue,
    required this.hasNoSalesActivity,
    required this.customerCreditDue,
    required this.netProfit,
    required this.netProfitChangePercent,
    required this.lowStockItemCount,
    required this.stockValue,
    required this.supplierPayables,
    required this.netExpense,
    required this.alerts,
    required this.salesTrendSeries,
    required this.revenueVsExpenses,
    required this.latestSales,
  });

  final DateTime generatedAt;
  final DateTime startDate;
  final DateTime endDate;
  final int salesCount;
  final double? salesRevenue;
  final bool hasNoSalesActivity;
  final double customerCreditDue;
  final double? netProfit;
  final double? netProfitChangePercent;
  final int lowStockItemCount;
  final double? stockValue;
  final double supplierPayables;
  final double? netExpense;
  final List<DashboardAlert> alerts;
  final List<DashboardSalesTrendPoint> salesTrendSeries;
  final List<DashboardRevenueVsExpensesPoint> revenueVsExpenses;
  final List<DashboardLatestSale> latestSales;

  @override
  List<Object?> get props => [
    generatedAt,
    startDate,
    endDate,
    salesCount,
    salesRevenue,
    hasNoSalesActivity,
    customerCreditDue,
    netProfit,
    netProfitChangePercent,
    lowStockItemCount,
    stockValue,
    supplierPayables,
    netExpense,
    alerts,
    salesTrendSeries,
    revenueVsExpenses,
    latestSales,
  ];
}

class DashboardAlert extends Equatable {
  const DashboardAlert({
    required this.alertType,
    required this.title,
    required this.message,
    required this.actionLabel,
    required this.actionRoute,
  });

  final String alertType;
  final String title;
  final String message;
  final String actionLabel;
  final String actionRoute;

  @override
  List<Object?> get props => [
    alertType,
    title,
    message,
    actionLabel,
    actionRoute,
  ];
}

class DashboardLatestSale extends Equatable {
  const DashboardLatestSale({
    required this.saleId,
    required this.invoiceNumber,
    required this.customerDisplayName,
    required this.soldAt,
    required this.totalAmount,
  });

  final String saleId;
  final String invoiceNumber;
  final String customerDisplayName;
  final DateTime soldAt;
  final double totalAmount;

  @override
  List<Object?> get props => [
    saleId,
    invoiceNumber,
    customerDisplayName,
    soldAt,
    totalAmount,
  ];
}

class DashboardSalesTrendPoint extends Equatable {
  const DashboardSalesTrendPoint({
    required this.date,
    required this.amount,
    required this.netAmount,
  });

  final DateTime date;
  final double amount;
  final double netAmount;

  @override
  List<Object?> get props => [date, amount, netAmount];
}

class DashboardRevenueVsExpensesPoint extends Equatable {
  const DashboardRevenueVsExpensesPoint({
    required this.date,
    required this.revenue,
    required this.expenses,
  });

  final DateTime date;
  final double revenue;
  final double expenses;

  @override
  List<Object?> get props => [date, revenue, expenses];
}

enum DashboardPeriod { last7, last30, custom }
