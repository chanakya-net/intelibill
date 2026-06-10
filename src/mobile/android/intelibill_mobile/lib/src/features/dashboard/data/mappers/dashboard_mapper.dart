import 'package:intelibill_mobile/src/features/dashboard/data/dto/dashboard_dto.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';

class DashboardMapper {
  static Dashboard toDomain(DashboardDto dto) {
    return Dashboard(
      generatedAt: DateTime.parse(dto.generatedAt),
      startDate: _parseDateOnly(dto.startDate),
      endDate: _parseDateOnly(dto.endDate),
      salesCount: dto.salesCount,
      salesRevenue: dto.salesRevenue,
      hasNoSalesActivity: dto.hasNoSalesActivity,
      customerCreditDue: dto.customerCreditDue,
      netProfit: dto.netProfit,
      netProfitChangePercent: dto.netProfitChangePercent,
      lowStockItemCount: dto.lowStockItemCount,
      stockValue: dto.stockValue,
      supplierPayables: dto.supplierPayables,
      netExpense: dto.netExpense,
      alerts: dto.alerts.map(_alertToDomain).toList(),
      salesTrendSeries:
          dto.salesTrendSeries?.map(_salesTrendToDomain).toList() ?? const [],
      revenueVsExpenses:
          dto.revenueVsExpenses?.map(_revenueVsExpensesToDomain).toList() ??
          const [],
      latestSales: dto.latestSales.map(_latestSaleToDomain).toList(),
    );
  }

  static DashboardAlert _alertToDomain(DashboardAlertDto dto) {
    return DashboardAlert(
      alertType: dto.alertType,
      title: dto.title,
      message: dto.message,
      actionLabel: dto.actionLabel,
      actionRoute: dto.actionRoute,
    );
  }

  static DashboardLatestSale _latestSaleToDomain(DashboardLatestSaleDto dto) {
    return DashboardLatestSale(
      saleId: dto.saleId,
      invoiceNumber: dto.invoiceNumber,
      customerDisplayName: dto.customerDisplayName,
      soldAt: DateTime.parse(dto.soldAt),
      totalAmount: dto.totalAmount,
    );
  }

  static DashboardSalesTrendPoint _salesTrendToDomain(
    DashboardSalesTrendPointDto dto,
  ) {
    return DashboardSalesTrendPoint(
      date: _parseDateOnly(dto.date),
      amount: dto.amount,
      netAmount: dto.netAmount,
    );
  }

  static DashboardRevenueVsExpensesPoint _revenueVsExpensesToDomain(
    DashboardRevenueVsExpensesPointDto dto,
  ) {
    return DashboardRevenueVsExpensesPoint(
      date: _parseDateOnly(dto.date),
      revenue: dto.revenue,
      expenses: dto.expenses,
    );
  }

  static DateTime _parseDateOnly(String value) {
    return DateTime.parse('${value}T00:00:00');
  }
}
