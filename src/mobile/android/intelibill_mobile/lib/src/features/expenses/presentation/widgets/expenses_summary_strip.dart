import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intl/intl.dart';

class ExpensesSummaryStrip extends StatelessWidget {
  const ExpensesSummaryStrip({
    required this.totalCount,
    required this.loadedAmount,
    required this.loadedActiveCount,
    required this.loadedVoidedCount,
    super.key,
  });

  final int totalCount;
  final double loadedAmount;
  final int loadedActiveCount;
  final int loadedVoidedCount;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final countFormat = NumberFormat.decimalPattern('en_IN');

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 4),
      child: Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          _SummaryMetric(
            label: l10n.expensesSummaryTotal,
            value: countFormat.format(totalCount),
          ),
          _SummaryMetric(
            label: l10n.expensesSummaryLoadedAmount,
            value: formatInr(loadedAmount),
          ),
          _SummaryMetric(
            label: l10n.expensesSummaryLoadedActive,
            value: countFormat.format(loadedActiveCount),
          ),
          _SummaryMetric(
            label: l10n.expensesSummaryLoadedVoided,
            value: countFormat.format(loadedVoidedCount),
          ),
        ],
      ),
    );
  }
}

class _SummaryMetric extends StatelessWidget {
  const _SummaryMetric({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      constraints: const BoxConstraints(minWidth: 96),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            value,
            style: theme.textTheme.titleSmall?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}
