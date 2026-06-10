import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/utils/sale_display_helpers.dart';
import 'package:intl/intl.dart';

class SaleListCard extends StatelessWidget {
  const SaleListCard({required this.sale, required this.onTap, super.key});

  final SaleListItem sale;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');
    final customerLabel = sale.customerName ?? l10n.salesHistoryWalkInCustomer;
    final paymentLabel = salePaymentMethodLabel(l10n, sale.paymentMethod);
    final statusLabel = saleStatusLabel(l10n, sale.status);
    final statusColor = saleStatusColor(theme, sale.status);
    final statusBackground = saleStatusBackground(theme, sale.status);
    final paymentColor = salePaymentMethodColor(theme, sale.paymentMethod);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          sale.invoiceNumber,
                          style: theme.textTheme.titleSmall?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          customerLabel,
                          style: theme.textTheme.bodyMedium,
                        ),
                        if (sale.customerPhone?.isNotEmpty == true) ...[
                          const SizedBox(height: 2),
                          Text(
                            sale.customerPhone!,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: theme.colorScheme.onSurfaceVariant,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: statusBackground,
                      borderRadius: BorderRadius.circular(20),
                    ),
                    child: Text(
                      statusLabel,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: statusColor,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Text(
                dateFormat.format(sale.soldAt),
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 6,
                children: [
                  _MetaChip(
                    label: paymentLabel,
                    color: paymentColor,
                  ),
                  _MetaChip(
                    label: l10n.salesHistoryItemCount(sale.itemCount),
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                  if (sale.returnNumbers.isNotEmpty)
                    _MetaChip(
                      label: sale.returnNumbers.join(', '),
                      color: theme.colorScheme.error,
                    ),
                ],
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          formatInr(sale.totalAmount),
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        if (sale.status == 'partiallyPaid' &&
                            sale.dueAmount > 0)
                          Text(
                            '${l10n.salesHistoryDueAmount}: '
                            '${formatInr(sale.dueAmount)}',
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: const Color(0xFFB45309),
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        if ((sale.status == 'refunded' ||
                                sale.status == 'returned') &&
                            sale.refundAmount > 0)
                          Text(
                            '${l10n.salesHistoryRefundAmount}: '
                            '${formatInr(sale.refundAmount)}',
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: theme.colorScheme.error,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                      ],
                    ),
                  ),
                  Icon(
                    Icons.chevron_right,
                    color: theme.colorScheme.onSurface.withValues(alpha: 0.4),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: color.withValues(alpha: 0.25)),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: color,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
