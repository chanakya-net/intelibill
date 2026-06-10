import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
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
    builder: (context) => SaleDetailSheet(sale: sale),
  );
}

class SaleDetailSheet extends StatelessWidget {
  const SaleDetailSheet({required this.sale, super.key});

  final SaleListItem sale;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');

    final rows = <_DetailRow>[
      _DetailRow(
        l10n.salesHistoryInvoiceNumber,
        sale.invoiceNumber,
      ),
      _DetailRow(
        l10n.salesHistoryCustomer,
        sale.customerName ?? l10n.salesHistoryWalkInCustomer,
      ),
      if (sale.customerPhone?.isNotEmpty == true)
        _DetailRow(l10n.salesHistoryPhone, sale.customerPhone!),
      _DetailRow(l10n.salesHistoryDate, dateFormat.format(sale.soldAt)),
      _DetailRow(
        l10n.salesHistoryControlsStatus,
        saleStatusLabel(l10n, sale.status),
      ),
      _DetailRow(
        l10n.salesHistoryPaymentMethod,
        salePaymentMethodLabel(l10n, sale.paymentMethod),
      ),
      _DetailRow(
        l10n.salesHistoryItemCountLabel,
        sale.itemCount.toString(),
      ),
      _DetailRow(l10n.salesHistoryTotal, formatInr(sale.totalAmount)),
      if (sale.dueAmount > 0)
        _DetailRow(l10n.salesHistoryDueAmount, formatInr(sale.dueAmount)),
      if (sale.refundAmount > 0)
        _DetailRow(
          l10n.salesHistoryRefundAmount,
          formatInr(sale.refundAmount),
        ),
    ];

    return SafeArea(
      child: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
        children: [
          Text(
            l10n.salesHistoryDetailTitle,
            style: theme.textTheme.titleLarge?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 16),
          for (final row in rows) ...[
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
          ],
        ],
      ),
    );
  }
}

class _DetailRow {
  const _DetailRow(this.label, this.value);

  final String label;
  final String value;
}
