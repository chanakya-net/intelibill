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
    final theme = Theme.of(context);
    final countFormat = NumberFormat.decimalPattern('en_IN');

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
      child: Card(
        elevation: 0,
        color: theme.colorScheme.primaryContainer.withValues(alpha: 0.35),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
          child: Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              _SummaryMetric(
                icon: Icons.receipt_long_outlined,
                label: l10n.expensesSummaryTotal,
                value: countFormat.format(totalCount),
              ),
              _SummaryMetric(
                icon: Icons.payments_outlined,
                label: l10n.expensesSummaryLoadedAmount,
                value: formatInr(loadedAmount),
                emphasize: true,
              ),
              _SummaryMetric(
                icon: Icons.check_circle_outline,
                label: l10n.expensesSummaryLoadedActive,
                value: countFormat.format(loadedActiveCount),
              ),
              _SummaryMetric(
                icon: Icons.cancel_outlined,
                label: l10n.expensesSummaryLoadedVoided,
                value: countFormat.format(loadedVoidedCount),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SummaryMetric extends StatelessWidget {
  const _SummaryMetric({
    required this.icon,
    required this.label,
    required this.value,
    this.emphasize = false,
  });

  final IconData icon;
  final String label;
  final String value;
  final bool emphasize;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Semantics(
      label: AppLocalizations.of(
        context,
      )!.expensesMetricSemantics(label, value),
      container: true,
      child: ExcludeSemantics(
        child: Container(
          constraints: const BoxConstraints(minWidth: 96),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
          decoration: BoxDecoration(
            color: theme.colorScheme.surface,
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: const Color(0xFFDFD1C4)),
            boxShadow: const [
              BoxShadow(
                color: Color(0x0A20130E),
                blurRadius: 2,
                offset: Offset(0, 1),
              ),
            ],
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                icon,
                size: 16,
                color: emphasize
                    ? theme.colorScheme.primary
                    : theme.colorScheme.onSurfaceVariant,
              ),
              const SizedBox(height: 4),
              Text(
                label,
                style: theme.textTheme.labelSmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 2),
              Text(
                value,
                style: theme.textTheme.titleSmall?.copyWith(
                  fontWeight: FontWeight.w800,
                  color: emphasize
                      ? theme.colorScheme.primary
                      : theme.colorScheme.onSurface,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
