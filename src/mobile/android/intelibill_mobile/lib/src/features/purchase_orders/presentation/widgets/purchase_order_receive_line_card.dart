import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';

class PurchaseOrderReceiveLineCard extends StatelessWidget {
  const PurchaseOrderReceiveLineCard({
    required this.line,
    required this.onSelectionChanged,
    required this.onQuantityChanged,
    required this.onBarcodeChanged,
    required this.onBatchNumberChanged,
    super.key,
  });

  static Key barcodeField(String lineId) => Key('receive-line-barcode-$lineId');
  static Key batchField(String lineId) => Key('receive-line-batch-$lineId');
  static Key selectionCheckbox(String lineId) =>
      Key('receive-line-selection-$lineId');
  static Key quantityField(String lineId) =>
      Key('receive-line-quantity-$lineId');

  final ReceivePurchaseOrderLineDraft line;
  final ValueChanged<bool> onSelectionChanged;
  final ValueChanged<String> onQuantityChanged;
  final ValueChanged<String> onBarcodeChanged;
  final ValueChanged<String> onBatchNumberChanged;

  static Key cardKey(String lineId) => Key('receive-line-card-$lineId');

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ExpansionTile(
        key: cardKey(line.purchaseOrderLineId),
        leading: Checkbox(
          key: selectionCheckbox(line.purchaseOrderLineId),
          value: line.isSelected,
          onChanged: (value) => onSelectionChanged(value ?? false),
        ),
        title: Text(
          line.description,
          style: Theme.of(context).textTheme.titleSmall,
        ),
        subtitle: Text('Remaining: ${line.remainingQuantity}'),
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                TextFormField(
                  key: quantityField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveLineQuantity,
                    helperText: 'Remaining: ${line.remainingQuantity}',
                  ),
                  initialValue: line.quantity.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.number,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  onChanged: onQuantityChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: barcodeField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveBarcodeLabel,
                  ),
                  initialValue: line.barcode,
                  enabled: line.isSelected,
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
                  enabled: line.isSelected,
                  onChanged: onBatchNumberChanged,
                  maxLength: 80,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
