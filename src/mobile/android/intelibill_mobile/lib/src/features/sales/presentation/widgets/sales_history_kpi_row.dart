import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intl/intl.dart';

class SalesHistoryKpiRow extends StatelessWidget {
  const SalesHistoryKpiRow({required this.summary, super.key});

  final SalesHistorySummary summary;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final invoiceCount = NumberFormat.decimalPattern('en_IN').format(
      summary.invoiceCount,
    );

    return Row(
      children: [
        Expanded(
          child: _KpiCard(
            label: l10n.salesHistoryKpiPeriodSales,
            value: formatInr(summary.periodSales),
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _KpiCard(
            label: l10n.salesHistoryKpiInvoices,
            value: invoiceCount,
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _KpiCard(
            label: l10n.salesHistoryKpiRefunds,
            value: formatInr(summary.refundAmount),
          ),
        ),
      ],
    );
  }
}

class _KpiCard extends StatelessWidget {
  const _KpiCard({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 12),
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
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.labelSmall?.copyWith(
              color: const Color(0xFF746961),
              fontWeight: FontWeight.w700,
              letterSpacing: 0.8,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w800,
              color: const Color(0xFF20130E),
            ),
          ),
        ],
      ),
    );
  }
}
