import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intl/intl.dart';

class SalesReceiptView extends StatelessWidget {
  const SalesReceiptView({
    required this.sale,
    this.formatter = formatInr,
    super.key,
  });

  final SaleDetail sale;
  final String Function(num?) formatter;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          l10n.salesDetailReceipt,
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 12),
        _ReceiptRow(
          label: l10n.salesHistoryInvoiceNumber,
          value: sale.invoiceNumber,
        ),
        _ReceiptRow(
          label: l10n.salesHistoryDate,
          value: dateFormat.format(sale.soldAt.toLocal()),
        ),
        if (sale.customerName != null)
          _ReceiptRow(
            label: l10n.salesHistoryCustomer,
            value: sale.customerName!,
          ),
        if (sale.customerPhone != null && sale.customerPhone!.trim().isNotEmpty)
          _ReceiptRow(
            label: l10n.salesHistoryPhone,
            value: sale.customerPhone!,
          ),
        const Divider(height: 24),
        if (sale.items.isNotEmpty) ...[
          _ReceiptSection(
            title: l10n.salesDetailLineItems,
            children: [
              for (final item in sale.items) ...[
                _LineItemRow(item: item, formatter: formatter),
                const SizedBox(height: 8),
              ],
            ],
          ),
          const SizedBox(height: 16),
        ],
        _ReceiptSection(
          title: l10n.salesDetailTotals,
          children: [
            _ReceiptRow(
              label: l10n.salesDetailBeforeDiscount,
              value: formatter(sale.totalBeforeDiscount),
            ),
            _ReceiptRow(
              label: l10n.salesDetailDiscountLabel,
              value: '- ${formatter(sale.totalDiscountAmount)}',
            ),
            _ReceiptRow(
              label: l10n.salesDetailTaxLabel,
              value: formatter(sale.totalTaxAmount),
            ),
            _ReceiptRow(
              label: l10n.salesHistoryTotal,
              value: formatter(sale.totalAmount),
              isEmphasized: true,
            ),
            _ReceiptRow(
              label: l10n.salesDetailPaidLabel,
              value: formatter(sale.paidAmount),
            ),
            if (sale.dueAmount > 0)
              _ReceiptRow(
                label: l10n.salesHistoryDueAmount,
                value: formatter(sale.dueAmount),
              ),
            if (sale.creditNoteAppliedAmount > 0)
              _ReceiptRow(
                label: l10n.salesReceiptCreditNoteApplied,
                value: formatter(sale.creditNoteAppliedAmount),
              ),
            if (sale.dueReductionAmount > 0)
              _ReceiptRow(
                label: l10n.salesDetailDueReduction,
                value: formatter(sale.dueReductionAmount),
              ),
          ],
        ),
        if (sale.discounts.isNotEmpty) ...[
          const SizedBox(height: 16),
          _ReceiptSection(
            title: l10n.salesDetailDiscounts,
            children: [
              for (final discount in sale.discounts) ...[
                _ReceiptRow(
                  label: '${discount.type} • ${discount.value}',
                  value: '- ${formatter(discount.amount)}',
                ),
                const SizedBox(height: 8),
              ],
            ],
          ),
        ],
        const SizedBox(height: 16),
        _ReceiptSection(
          title: l10n.salesDetailPaymentSplit,
          children: [
            if (sale.settlements.isEmpty) ...[
              Text(
                l10n.salesDetailNoSettlementRecords,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ] else ...[
              for (final settlement in sale.settlements) ...[
                _ReceiptRow(
                  label: settlement.method,
                  value: formatter(settlement.amount),
                ),
                const SizedBox(height: 8),
              ],
            ],
          ],
        ),
        if (sale.creditNoteRedemptions.isNotEmpty) ...[
          const SizedBox(height: 16),
          _ReceiptSection(
            title: l10n.salesDetailRedemptions,
            children: [
              for (final redemption in sale.creditNoteRedemptions) ...[
                _ReceiptRow(
                  label: redemption.code,
                  value: formatter(redemption.amount),
                ),
                const SizedBox(height: 8),
              ],
            ],
          ),
        ],
      ],
    );
  }
}

class _ReceiptSection extends StatelessWidget {
  const _ReceiptSection({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          title,
          style: Theme.of(
            context,
          ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 12),
        ...children,
      ],
    );
  }
}

class _ReceiptRow extends StatelessWidget {
  const _ReceiptRow({
    required this.label,
    required this.value,
    this.isEmphasized = false,
  });

  final String label;
  final String value;
  final bool isEmphasized;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        children: [
          Expanded(
            child: Text(
              label,
              style: isEmphasized
                  ? theme.bodyLarge?.copyWith(fontWeight: FontWeight.w600)
                  : theme.bodyMedium?.copyWith(color: theme.bodySmall?.color),
            ),
          ),
          Text(
            value,
            style: isEmphasized
                ? theme.titleMedium?.copyWith(fontWeight: FontWeight.w700)
                : theme.bodyMedium,
          ),
        ],
      ),
    );
  }
}

class _LineItemRow extends StatelessWidget {
  const _LineItemRow({required this.item, required this.formatter});

  final SaleDetailItem item;
  final String Function(num?) formatter;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Text(item.name, style: Theme.of(context).textTheme.bodyMedium),
        ),
        const SizedBox(width: 8),
        Text(
          '${item.quantity.toStringAsFixed(item.quantity % 1 == 0 ? 0 : 2)} × ${item.rate.toStringAsFixed(2)}',
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(width: 8),
        Text(
          formatter(item.total),
          style: Theme.of(
            context,
          ).textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
        ),
      ],
    );
  }
}
