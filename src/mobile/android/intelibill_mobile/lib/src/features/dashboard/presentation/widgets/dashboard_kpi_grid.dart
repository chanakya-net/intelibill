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
    final colorScheme = theme.colorScheme;

    final cards = <_KpiCardData>[
      _KpiCardData(
        label: l10n.dashboardKpiSalesRevenue,
        value: formatInr(dashboard.salesRevenue),
        accent: _KpiAccent.primary,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiNetProfit,
        value: formatInr(dashboard.netProfit),
        accent: _KpiAccent.secondary,
        changePercent: dashboard.netProfitChangePercent,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiInvoiceCount,
        value: dashboard.salesCount.toString(),
        accent: _KpiAccent.neutral,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiLowStockItems,
        value: dashboard.lowStockItemCount.toString(),
        accent: _KpiAccent.error,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiStockValue,
        value: formatInr(dashboard.stockValue),
        accent: _KpiAccent.primary,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiCustomerCreditDue,
        value: formatInr(dashboard.customerCreditDue),
        accent: _KpiAccent.secondary,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiSupplierPayables,
        value: formatInr(dashboard.supplierPayables),
        accent: _KpiAccent.neutral,
      ),
      _KpiCardData(
        label: l10n.dashboardKpiExpenses,
        value: formatInr(dashboard.netExpense),
        accent: _KpiAccent.error,
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
        final accentColor = _accentColor(colorScheme, card.accent);

        return Card(
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      width: 4,
                      height: 28,
                      decoration: BoxDecoration(
                        color: accentColor,
                        borderRadius: BorderRadius.circular(4),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        card.label,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.labelMedium?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                  ],
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
                          ? colorScheme.primary
                          : colorScheme.error,
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

  Color _accentColor(ColorScheme colorScheme, _KpiAccent accent) {
    return switch (accent) {
      _KpiAccent.primary => colorScheme.primary,
      _KpiAccent.secondary => colorScheme.secondary,
      _KpiAccent.error => colorScheme.error,
      _KpiAccent.neutral => colorScheme.onSurface,
    };
  }
}

enum _KpiAccent { primary, secondary, error, neutral }

class _KpiCardData {
  const _KpiCardData({
    required this.label,
    required this.value,
    required this.accent,
    this.changePercent,
  });

  final String label;
  final String value;
  final _KpiAccent accent;
  final double? changePercent;
}
