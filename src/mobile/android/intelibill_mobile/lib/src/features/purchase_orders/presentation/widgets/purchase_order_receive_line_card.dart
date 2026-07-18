import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';

class PurchaseOrderReceiveLineCard extends StatelessWidget {
  const PurchaseOrderReceiveLineCard({
    required this.line,
    required this.onBarcodeChanged,
    required this.onBatchNumberChanged,
    super.key,
  });

  static Key barcodeField(String lineId) => Key('receive-line-barcode-$lineId');
  static Key batchField(String lineId) => Key('receive-line-batch-$lineId');

  final ReceivePurchaseOrderLineDraft line;
  final ValueChanged<String> onBarcodeChanged;
  final ValueChanged<String> onBatchNumberChanged;

  static Key cardKey(String lineId) => Key('receive-line-card-$lineId');

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          key: cardKey(line.purchaseOrderLineId),
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              line.description,
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: 8),
            Text('${l10n.purchaseOrderReceiveLineQuantity}: ${line.quantity}'),
            const SizedBox(height: 10),
            TextFormField(
              key: barcodeField(line.purchaseOrderLineId),
              decoration: InputDecoration(
                labelText: l10n.purchaseOrderReceiveBarcodeLabel,
              ),
              initialValue: line.barcode,
              onChanged: onBarcodeChanged,
              maxLength: 120,
            ),
            const SizedBox(height: 8),
            TextFormField(
              key: batchField(line.purchaseOrderLineId),
              decoration: InputDecoration(
                labelText: l10n.purchaseOrderReceiveBatchLabel,
              ),
              initialValue: line.batchNumber,
              onChanged: onBatchNumberChanged,
              maxLength: 80,
            ),
          ],
        ),
      ),
    );
  }
}
