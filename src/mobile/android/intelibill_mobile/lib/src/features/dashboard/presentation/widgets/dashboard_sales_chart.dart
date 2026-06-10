import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';
import 'package:intl/intl.dart';

class DashboardSalesChart extends StatelessWidget {
  const DashboardSalesChart({required this.dashboard, super.key});

  final Dashboard dashboard;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final salesTrend = dashboard.salesTrendSeries;
    final revenueVsExpenses = dashboard.revenueVsExpenses;

    return Column(
      children: [
        if (salesTrend.isNotEmpty)
          _ChartCard(
            title: l10n.dashboardChartSalesTrend,
            subtitle: l10n.dashboardChartSalesTrendSubtitle,
            child: _BarChart(
              labels: salesTrend
                  .map((point) => _formatChartDate(point.date))
                  .toList(),
              values: salesTrend.map((point) => point.amount).toList(),
              barColor: const Color(0xFFF27A20),
            ),
          ),
        if (revenueVsExpenses.isNotEmpty) ...[
          const SizedBox(height: 12),
          _ChartCard(
            title: l10n.dashboardChartRevenueVsExpenses,
            subtitle: l10n.dashboardChartRevenueVsExpensesSubtitle,
            child: _GroupedBarChart(
              labels: revenueVsExpenses
                  .map((point) => _formatChartDate(point.date))
                  .toList(),
              revenueValues: revenueVsExpenses
                  .map((point) => point.revenue)
                  .toList(),
              expenseValues: revenueVsExpenses
                  .map((point) => point.expenses)
                  .toList(),
              revenueLabel: l10n.dashboardChartRevenue,
              expensesLabel: l10n.dashboardChartExpenses,
            ),
          ),
        ],
        if (salesTrend.isEmpty && revenueVsExpenses.isEmpty)
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Text(
                l10n.dashboardNoChartData,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ),
          ),
      ],
    );
  }

  String _formatChartDate(DateTime date) {
    return DateFormat('d MMM').format(date);
  }
}

class _ChartCard extends StatelessWidget {
  const _ChartCard({
    required this.title,
    required this.subtitle,
    required this.child,
  });

  final String title;
  final String subtitle;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: theme.textTheme.titleMedium),
            const SizedBox(height: 4),
            Text(
              subtitle,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            SizedBox(height: 220, child: child),
          ],
        ),
      ),
    );
  }
}

class _BarChart extends StatelessWidget {
  const _BarChart({
    required this.labels,
    required this.values,
    required this.barColor,
  });

  final List<String> labels;
  final List<double> values;
  final Color barColor;

  @override
  Widget build(BuildContext context) {
    final maxValue = values.fold<double>(
      0,
      (max, value) => value > max ? value : max,
    );

    return BarChart(
      BarChartData(
        maxY: maxValue <= 0 ? 1 : maxValue * 1.15,
        gridData: FlGridData(
          drawVerticalLine: false,
          getDrawingHorizontalLine: (value) => FlLine(
            color: Colors.grey.withValues(alpha: 0.15),
            strokeWidth: 1,
          ),
        ),
        borderData: FlBorderData(show: false),
        titlesData: FlTitlesData(
          topTitles: const AxisTitles(),
          rightTitles: const AxisTitles(),
          leftTitles: AxisTitles(
            sideTitles: SideTitles(
              reservedSize: 52,
              showTitles: true,
              getTitlesWidget: (value, meta) {
                if (value == meta.max) {
                  return const SizedBox.shrink();
                }
                return Text(
                  formatInr(value),
                  style: const TextStyle(fontSize: 10),
                );
              },
            ),
          ),
          bottomTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              reservedSize: 28,
              getTitlesWidget: (value, meta) {
                final index = value.toInt();
                if (index < 0 || index >= labels.length) {
                  return const SizedBox.shrink();
                }
                return Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    labels[index],
                    style: const TextStyle(fontSize: 10),
                  ),
                );
              },
            ),
          ),
        ),
        barGroups: [
          for (var i = 0; i < values.length; i++)
            BarChartGroupData(
              x: i,
              barRods: [
                BarChartRodData(
                  toY: values[i],
                  color: barColor.withValues(alpha: 0.82),
                  width: 14,
                  borderRadius: const BorderRadius.vertical(
                    top: Radius.circular(4),
                  ),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class _GroupedBarChart extends StatelessWidget {
  const _GroupedBarChart({
    required this.labels,
    required this.revenueValues,
    required this.expenseValues,
    required this.revenueLabel,
    required this.expensesLabel,
  });

  final List<String> labels;
  final List<double> revenueValues;
  final List<double> expenseValues;
  final String revenueLabel;
  final String expensesLabel;

  @override
  Widget build(BuildContext context) {
    final maxValue = [
      ...revenueValues,
      ...expenseValues,
    ].fold<double>(0, (max, value) => value > max ? value : max);

    return Column(
      children: [
        Row(
          children: [
            _LegendDot(color: const Color(0xFFF27A20), label: revenueLabel),
            const SizedBox(width: 16),
            _LegendDot(color: const Color(0xFF8B7355), label: expensesLabel),
          ],
        ),
        const SizedBox(height: 12),
        Expanded(
          child: BarChart(
            BarChartData(
              maxY: maxValue <= 0 ? 1 : maxValue * 1.15,
              gridData: FlGridData(
                drawVerticalLine: false,
                getDrawingHorizontalLine: (value) => FlLine(
                  color: Colors.grey.withValues(alpha: 0.15),
                  strokeWidth: 1,
                ),
              ),
              borderData: FlBorderData(show: false),
              titlesData: FlTitlesData(
                topTitles: const AxisTitles(),
                rightTitles: const AxisTitles(),
                leftTitles: AxisTitles(
                  sideTitles: SideTitles(
                    reservedSize: 52,
                    showTitles: true,
                    getTitlesWidget: (value, meta) {
                      if (value == meta.max) {
                        return const SizedBox.shrink();
                      }
                      return Text(
                        formatInr(value),
                        style: const TextStyle(fontSize: 10),
                      );
                    },
                  ),
                ),
                bottomTitles: AxisTitles(
                  sideTitles: SideTitles(
                    showTitles: true,
                    reservedSize: 28,
                    getTitlesWidget: (value, meta) {
                      final index = value.toInt();
                      if (index < 0 || index >= labels.length) {
                        return const SizedBox.shrink();
                      }
                      return Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          labels[index],
                          style: const TextStyle(fontSize: 10),
                        ),
                      );
                    },
                  ),
                ),
              ),
              barGroups: [
                for (var i = 0; i < labels.length; i++)
                  BarChartGroupData(
                    x: i,
                    barsSpace: 4,
                    barRods: [
                      BarChartRodData(
                        toY: revenueValues[i],
                        color: const Color(0xFFF27A20).withValues(alpha: 0.82),
                        width: 10,
                        borderRadius: const BorderRadius.vertical(
                          top: Radius.circular(4),
                        ),
                      ),
                      BarChartRodData(
                        toY: expenseValues[i],
                        color: const Color(0xFF8B7355).withValues(alpha: 0.78),
                        width: 10,
                        borderRadius: const BorderRadius.vertical(
                          top: Radius.circular(4),
                        ),
                      ),
                    ],
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _LegendDot extends StatelessWidget {
  const _LegendDot({required this.color, required this.label});

  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 6),
        Text(label, style: Theme.of(context).textTheme.labelSmall),
      ],
    );
  }
}
