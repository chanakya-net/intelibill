import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/utils/sale_display_helpers.dart';
import 'package:intl/intl.dart';

Future<void> showSaleDetailSheet(
  BuildContext context, {
  required SaleListItem sale,
}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (context) => SaleDetailSheet(saleId: sale.saleId),
  );
}

class SaleDetailSheet extends ConsumerWidget {
  const SaleDetailSheet({required this.saleId, super.key});

  final String saleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(saleDetailControllerProvider(saleId));
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);

    if (state.isLoading) {
      return SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: const Center(child: CircularProgressIndicator()),
        ),
      );
    }

    if (state.failure != null) {
      return SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline, size: 48),
                const SizedBox(height: 12),
                Text(
                  l10n.salesHistoryUnableToLoad,
                  style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: () {
                    ref
                        .read(saleDetailControllerProvider(saleId).notifier)
                        .refresh(saleId);
                  },
                  child: Text(l10n.salesHistoryRetry),
                ),
              ],
            ),
          ),
        ),
      );
    }

    final detail = state.detail;
    if (detail == null) {
      return SafeArea(
        child: Center(
          child: Text(l10n.salesHistoryNoSales),
        ),
      );
    }

    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');
    return SafeArea(
      child: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  l10n.salesHistoryDetailTitle,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
              IconButton(
                icon: const Icon(Icons.refresh),
                onPressed: () {
                  ref
                      .read(saleDetailControllerProvider(saleId).notifier)
                      .refresh(saleId);
                },
              ),
            ],
          ),
          const SizedBox(height: 16),
          ..._buildDetailRows(
            l10n,
            theme,
            detail,
            dateFormat,
          ),
        ],
      ),
    );
  }

  List<Widget> _buildDetailRows(
    AppLocalizations l10n,
    ThemeData theme,
    SaleDetail detail,
    DateFormat dateFormat,
  ) {
    final rows = <_DetailRow>[
      _DetailRow(l10n.salesHistoryInvoiceNumber, detail.invoiceNumber),
      _DetailRow(
        l10n.salesHistoryCustomer,
        detail.customerName ?? l10n.salesHistoryWalkInCustomer,
      ),
      if (detail.customerPhone?.isNotEmpty == true)
        _DetailRow(l10n.salesHistoryPhone, detail.customerPhone!),
      _DetailRow(l10n.salesHistoryDate, dateFormat.format(detail.soldAt)),
      _DetailRow(
        l10n.salesHistoryControlsStatus,
        saleStatusLabel(l10n, detail.status),
      ),
      _DetailRow(
        l10n.salesHistoryPaymentMethod,
        salePaymentMethodLabel(l10n, detail.paymentMethod),
      ),
      _DetailRow(
        l10n.salesHistoryItemCountLabel,
        detail.items.length.toString(),
      ),
      _DetailRow(l10n.salesHistoryTotal, formatInr(detail.totalAmount)),
      if (detail.dueAmount > 0)
        _DetailRow(l10n.salesHistoryDueAmount, formatInr(detail.dueAmount)),
      if (detail.refundAmount > 0)
        _DetailRow(
          l10n.salesHistoryRefundAmount,
          formatInr(detail.refundAmount),
        ),
    ];

    final widgets = <Widget>[];
    for (final row in rows) {
      widgets.addAll([
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              flex: 2,
              child: Text(
                row.label,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ),
            Expanded(
              flex: 3,
              child: Text(
                row.value,
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
        const Divider(height: 20),
      ]);
    }

    if (detail.items.isNotEmpty) {
      widgets.addAll([
        const SizedBox(height: 16),
        Text(
          'Line Items',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        ...detail.items.map((item) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.name,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      '${item.quantity} × ${formatInr(item.rate)}',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              Text(
                formatInr(item.total),
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
        )),
      ]);
    }

    if (detail.discounts.isNotEmpty) {
      widgets.addAll([
        const SizedBox(height: 16),
        Text(
          'Discounts',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        ...detail.discounts.map((discount) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  '${discount.type} (${discount.value})',
                  style: theme.textTheme.bodyMedium,
                ),
              ),
              Text(
                '- ${formatInr(discount.amount)}',
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
        )),
      ]);
    }

    if (detail.returns.isNotEmpty) {
      widgets.addAll([
        const SizedBox(height: 16),
        Text(
          'Returns',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        ...detail.returns.map((ret) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      ret.returnNumber,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  Text(
                    formatInr(ret.amount),
                    style: theme.textTheme.bodyMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(
                dateFormat.format(ret.returnedAt),
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        )),
      ]);
    }

    if (detail.settlements.isNotEmpty) {
      widgets.addAll([
        const SizedBox(height: 16),
        Text(
          'Payment Methods',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        ...detail.settlements.map((settlement) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      settlement.method,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      dateFormat.format(settlement.settledAt),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              Text(
                formatInr(settlement.amount),
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
        )),
      ]);
    }

    if (detail.warnings.isNotEmpty) {
      widgets.addAll([
        const SizedBox(height: 16),
        Text(
          'Warnings',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
            color: theme.colorScheme.error,
          ),
        ),
        const SizedBox(height: 12),
        ...detail.warnings.map((warning) => Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                Icons.warning_amber,
                size: 20,
                color: theme.colorScheme.error,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  warning.message,
                  style: theme.textTheme.bodyMedium,
                ),
              ),
            ],
          ),
        )),
      ]);
    }

    return widgets;
  }
}

class _DetailRow {
  const _DetailRow(this.label, this.value);

  final String label;
  final String value;
}
