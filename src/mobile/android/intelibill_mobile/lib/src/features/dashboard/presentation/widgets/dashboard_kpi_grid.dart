import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';

class DashboardKpiGrid extends StatelessWidget {
  const DashboardKpiGrid({required this.dashboard, super.key});

  final Dashboard dashboard;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);

    final cards = <_KpiCardData>[
      _KpiCardData(
        label: l10n.dashboardKpiSalesRevenue,
        value: formatInr(dashboard.salesRevenue),
        color: const Color(0xFFF27A20),
      ),
      _KpiCardData(
        label: l10n.dashboardKpiNetProfit,
        value: formatInr(dashboard.netProfit),
        color: const Color(0xFF6B8F71),
        changePercent: dashboard.netProfitChangePercent,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiInvoiceCount,
        value: dashboard.salesCount.toString(),
        color: const Color(0xFF8B7355),
      ),
      _KpiCardData(
        label: l10n.dashboardKpiLowStockItems,
        value: dashboard.lowStockItemCount.toString(),
        color: const Color(0xFFB85C6D),
      ),
      _KpiCardData(
        label: l10n.dashboardKpiStockValue,
        value: formatInr(dashboard.stockValue),
        color: const Color(0xFFF27A20),
      ),
      _KpiCardData(
        label: l10n.dashboardKpiCustomerCreditDue,
        value: formatInr(dashboard.customerCreditDue),
        color: const Color(0xFFF27A20),
      ),
      _KpiCardData(
        label: l10n.dashboardKpiSupplierPayables,
        value: formatInr(dashboard.supplierPayables),
        color: const Color(0xFF8B7355),
      ),
      _KpiCardData(
        label: l10n.dashboardKpiExpenses,
        value: formatInr(dashboard.netExpense),
        color: const Color(0xFFB85C6D),
      ),
    ];

    return GridView.builder(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      itemCount: cards.length,
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 2,
        crossAxisSpacing: 12,
        mainAxisSpacing: 12,
        childAspectRatio: 1.45,
      ),
      itemBuilder: (context, index) {
        final card = cards[index];
        return Card(
          elevation: 0,
          color: card.color.withValues(alpha: 0.08),
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  card.label,
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                const Spacer(),
                Text(
                  card.value,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
                if (card.changePercent != null) ...[
                  const SizedBox(height: 4),
                  Text(
                    '${card.changePercent! > 0 ? '+' : ''}'
                    '${card.changePercent!.toStringAsFixed(1)}% '
                    '${l10n.dashboardKpiVsPreviousPeriod}',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: card.changePercent! >= 0
                          ? Colors.green.shade700
                          : theme.colorScheme.error,
                    ),
                  ),
                ],
              ],
            ),
          ),
        );
      },
    );
  }
}

class _KpiCardData {
  const _KpiCardData({
    required this.label,
    required this.value,
    required this.color,
    this.changePercent,
  });

  final String label;
  final String value;
  final Color color;
  final double? changePercent;
}
