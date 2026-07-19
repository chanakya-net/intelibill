import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt_line.dart';
import 'package:intl/intl.dart';

class PurchaseOrderReceiptHistory extends StatelessWidget {
  const PurchaseOrderReceiptHistory({required this.receipts, super.key});

  final List<PurchaseOrderReceipt> receipts;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final locale = Localizations.localeOf(context).toLanguageTag();
    final dateFormat = DateFormat.yMMMd(locale).add_jm();
    return Card(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
            child: Text(
              l10n.purchaseOrderReceiptHistory,
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ),
          if (receipts.isEmpty)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
              child: Text(l10n.purchaseOrderNoReceipts),
            )
          else
            ...receipts.map(
              (receipt) => ExpansionTile(
                title: Text(receipt.receiptNumber),
                subtitle: Text(
                  '${l10n.purchaseOrderReceiptReceivedAt}: '
                  '${dateFormat.format(receipt.receivedAt)}',
                ),
                children: [
                  _ReceiptSummary(receipt: receipt),
                  ...receipt.lines.map(
                    (line) => _ReceiptLineDetails(line: line),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

class _ReceiptSummary extends StatelessWidget {
  const _ReceiptSummary({required this.receipt});

  final PurchaseOrderReceipt receipt;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final receiver = receipt.receivedByDisplayName?.isNotEmpty == true
        ? receipt.receivedByDisplayName!
        : receipt.receivedByUserId;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('${l10n.purchaseOrderReceiptReceivedBy}: $receiver'),
          if (receipt.referenceNumber?.isNotEmpty == true)
            Text(
              '${l10n.purchaseOrderReceiptReference}: '
              '${receipt.referenceNumber}',
            ),
          if (receipt.notes?.isNotEmpty == true)
            Text('${l10n.purchaseOrderReceiptNotes}: ${receipt.notes}'),
        ],
      ),
    );
  }
}

class _ReceiptLineDetails extends StatelessWidget {
  const _ReceiptLineDetails({required this.line});

  final PurchaseOrderReceiptLine line;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Padding(
      padding: const EdgeInsets.fromLTRB(32, 8, 16, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (line.batchNumber?.isNotEmpty == true)
            Text('${l10n.purchaseOrderReceiptBatch}: ${line.batchNumber}'),
          if (line.batchVoided != null)
            Text(
              '${l10n.purchaseOrderReceiptBatchState}: '
              '${line.batchVoided! ? l10n.purchaseOrderReceiptVoided : l10n.purchaseOrderReceiptActive}',
            ),
          Text(
            '${l10n.purchaseOrderReceiptStockTransaction}: '
            '${line.stockTransactionId}',
          ),
          Text('${l10n.purchaseOrderReceiptQuantity}: ${line.quantity}'),
          Text(
            '${l10n.purchaseOrderReceiptTotalPurchaseCost}: '
            '${formatInr(line.totalPurchaseCost)}',
          ),
          Text(
            '${l10n.purchaseOrderReceiptUnitCost}: ${formatInr(line.unitCost)}',
          ),
          Text('${l10n.purchaseOrderReceiptMrp}: ${formatInr(line.mrp)}'),
          Text(
            '${l10n.purchaseOrderReceiptSalesPrice}: '
            '${formatInr(line.salesPrice)}',
          ),
          Text(
            '${l10n.purchaseOrderReceiptTaxRate}: '
            '${_formatNumber(line.taxRatePercent)}%',
          ),
          Text(
            '${l10n.purchaseOrderReceiptTaxIncluded}: '
            '${line.taxIncluded ? l10n.purchaseOrderReceiptYes : l10n.purchaseOrderReceiptNo}',
          ),
          Text(
            '${l10n.purchaseOrderReceiptPurchaseTaxIncluded}: '
            '${line.purchaseTaxIncluded ? l10n.purchaseOrderReceiptYes : l10n.purchaseOrderReceiptNo}',
          ),
        ],
      ),
    );
  }

  String _formatNumber(double value) =>
      value.truncateToDouble() == value ? value.toInt().toString() : '$value';
}
