import 'package:intelibill_mobile/src/features/dashboard/data/dto/dashboard_dto.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';

const dashboardJson = <String, dynamic>{
  'generatedAt': '2026-06-10T10:00:00.000Z',
  'startDate': '2026-05-12',
  'endDate': '2026-06-10',
  'salesCount': 5,
  'salesRevenue': 12000,
  'hasNoSalesActivity': false,
  'customerCreditDue': 500,
  'salesBooked': 12000,
  'netSalesBooked': 11500,
  'netProfit': 3000,
  'netProfitChangePercent': 12.5,
  'netExpense': 1500,
  'supplierPayables': 800,
  'runningLowStockCount': 2,
  'lowStockItemCount': 2,
  'criticalStockCount': 0,
  'rankedShortageList': <dynamic>[],
  'alerts': [
    {
      'alertType': 'LowStock',
      'priority': 1,
      'title': 'Low Stock',
      'message': '2 items are running low.',
      'actionLabel': 'Manage inventory',
      'actionRoute': '/inventory',
    },
  ],
  'salesTrendSeries': [
    {'date': '2026-06-09', 'amount': 4000, 'netAmount': 3800},
    {'date': '2026-06-10', 'amount': 5000, 'netAmount': 4700},
  ],
  'revenueVsExpenses': [
    {'date': '2026-06-09', 'revenue': 4000, 'expenses': 500},
    {'date': '2026-06-10', 'revenue': 5000, 'expenses': 600},
  ],
  'latestSales': [
    {
      'saleId': 'sale-1',
      'invoiceNumber': 'INV-001',
      'customerDisplayName': 'Walk-in Customer',
      'soldAt': '2026-06-10T09:30:00.000Z',
      'totalAmount': 1500,
    },
  ],
  'stockValue': 45000,
};

final dashboardDto = DashboardDto(
  generatedAt: '2026-06-10T10:00:00.000Z',
  startDate: '2026-05-12',
  endDate: '2026-06-10',
  salesCount: 5,
  salesRevenue: 12000,
  hasNoSalesActivity: false,
  customerCreditDue: 500,
  netProfit: 3000,
  netProfitChangePercent: 12.5,
  runningLowStockCount: 2,
  lowStockItemCount: 2,
  criticalStockCount: 0,
  stockValue: 45000,
  supplierPayables: 800,
  netExpense: 1500,
  alerts: [
    DashboardAlertDto(
      alertType: 'LowStock',
      priority: 1,
      title: 'Low Stock',
      message: '2 items are running low.',
      actionLabel: 'Manage inventory',
      actionRoute: '/inventory',
    ),
  ],
  salesTrendSeries: [
    DashboardSalesTrendPointDto(
      date: '2026-06-09',
      amount: 4000,
      netAmount: 3800,
    ),
    DashboardSalesTrendPointDto(
      date: '2026-06-10',
      amount: 5000,
      netAmount: 4700,
    ),
  ],
  revenueVsExpenses: [
    DashboardRevenueVsExpensesPointDto(
      date: '2026-06-09',
      revenue: 4000,
      expenses: 500,
    ),
    DashboardRevenueVsExpensesPointDto(
      date: '2026-06-10',
      revenue: 5000,
      expenses: 600,
    ),
  ],
  latestSales: [
    DashboardLatestSaleDto(
      saleId: 'sale-1',
      invoiceNumber: 'INV-001',
      customerDisplayName: 'Walk-in Customer',
      soldAt: '2026-06-10T09:30:00.000Z',
      totalAmount: 1500,
    ),
  ],
);

final testDashboard = Dashboard(
  generatedAt: DateTime.parse('2026-06-10T10:00:00.000Z'),
  startDate: DateTime.parse('2026-05-12T00:00:00'),
  endDate: DateTime.parse('2026-06-10T00:00:00'),
  salesCount: 5,
  salesRevenue: 12000,
  hasNoSalesActivity: false,
  customerCreditDue: 500,
  netProfit: 3000,
  netProfitChangePercent: 12.5,
  lowStockItemCount: 2,
  stockValue: 45000,
  supplierPayables: 800,
  netExpense: 1500,
  alerts: const [
    DashboardAlert(
      alertType: 'LowStock',
      title: 'Low Stock',
      message: '2 items are running low.',
      actionLabel: 'Manage inventory',
      actionRoute: '/inventory',
    ),
  ],
  salesTrendSeries: [
    DashboardSalesTrendPoint(
      date: DateTime.parse('2026-06-09T00:00:00'),
      amount: 4000,
      netAmount: 3800,
    ),
    DashboardSalesTrendPoint(
      date: DateTime.parse('2026-06-10T00:00:00'),
      amount: 5000,
      netAmount: 4700,
    ),
  ],
  revenueVsExpenses: [
    DashboardRevenueVsExpensesPoint(
      date: DateTime.parse('2026-06-09T00:00:00'),
      revenue: 4000,
      expenses: 500,
    ),
    DashboardRevenueVsExpensesPoint(
      date: DateTime.parse('2026-06-10T00:00:00'),
      revenue: 5000,
      expenses: 600,
    ),
  ],
  latestSales: [
    DashboardLatestSale(
      saleId: 'sale-1',
      invoiceNumber: 'INV-001',
      customerDisplayName: 'Walk-in Customer',
      soldAt: DateTime.parse('2026-06-10T09:30:00.000Z'),
      totalAmount: 1500,
    ),
  ],
);
