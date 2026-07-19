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
    required this.onTotalPurchaseCostChanged,
    required this.onMrpChanged,
    required this.onSalesPriceChanged,
    required this.onTaxRatePercentChanged,
    required this.onTaxIncludedChanged,
    required this.onPurchaseTaxIncludedChanged,
    required this.onExpiryDateChanged,
    required this.onManufacturingDateChanged,
    super.key,
  });

  static Key barcodeField(String lineId) => Key('receive-line-barcode-$lineId');
  static Key batchField(String lineId) => Key('receive-line-batch-$lineId');
  static Key selectionCheckbox(String lineId) =>
      Key('receive-line-selection-$lineId');
  static Key quantityField(String lineId) =>
      Key('receive-line-quantity-$lineId');
  static Key totalCostField(String lineId) =>
      Key('receive-line-total-cost-$lineId');
  static Key mrpField(String lineId) => Key('receive-line-mrp-$lineId');
  static Key salesPriceField(String lineId) =>
      Key('receive-line-sales-price-$lineId');
  static Key taxRateField(String lineId) =>
      Key('receive-line-tax-rate-$lineId');
  static Key taxIncludedCheckbox(String lineId) =>
      Key('receive-line-tax-included-$lineId');
  static Key purchaseTaxIncludedCheckbox(String lineId) =>
      Key('receive-line-purchase-tax-included-$lineId');
  static Key expiryDateField(String lineId) =>
      Key('receive-line-expiry-date-$lineId');
  static Key manufacturingDateField(String lineId) =>
      Key('receive-line-manufacturing-date-$lineId');

  final ReceivePurchaseOrderLineDraft line;
  final ValueChanged<bool> onSelectionChanged;
  final ValueChanged<String> onQuantityChanged;
  final ValueChanged<String> onBarcodeChanged;
  final ValueChanged<String> onBatchNumberChanged;
  final ValueChanged<String> onTotalPurchaseCostChanged;
  final ValueChanged<String> onMrpChanged;
  final ValueChanged<String> onSalesPriceChanged;
  final ValueChanged<String> onTaxRatePercentChanged;
  final ValueChanged<bool> onTaxIncludedChanged;
  final ValueChanged<bool> onPurchaseTaxIncludedChanged;
  final ValueChanged<DateTime?> onExpiryDateChanged;
  final ValueChanged<DateTime?> onManufacturingDateChanged;

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
                const SizedBox(height: 16),
                Text(
                  'Inventory Details',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: totalCostField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: 'Total Purchase Cost',
                  ),
                  initialValue: line.totalPurchaseCost.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  inputFormatters: [
                    FilteringTextInputFormatter.allow(
                      RegExp(r'^\d+\.?\d{0,2}'),
                    ),
                  ],
                  onChanged: onTotalPurchaseCostChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: mrpField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: 'MRP',
                  ),
                  initialValue: line.mrp.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  inputFormatters: [
                    FilteringTextInputFormatter.allow(
                      RegExp(r'^\d+\.?\d{0,2}'),
                    ),
                  ],
                  onChanged: onMrpChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: salesPriceField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: 'Sales Price',
                  ),
                  initialValue: line.salesPrice.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  inputFormatters: [
                    FilteringTextInputFormatter.allow(
                      RegExp(r'^\d+\.?\d{0,2}'),
                    ),
                  ],
                  onChanged: onSalesPriceChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: taxRateField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: 'Tax Rate %',
                  ),
                  initialValue: line.taxRatePercent.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  inputFormatters: [
                    FilteringTextInputFormatter.allow(
                      RegExp(r'^\d+\.?\d{0,2}'),
                    ),
                  ],
                  onChanged: onTaxRatePercentChanged,
                ),
                const SizedBox(height: 12),
                CheckboxListTile(
                  key: taxIncludedCheckbox(line.purchaseOrderLineId),
                  title: const Text('Tax Included in Sales Price'),
                  value: line.taxIncluded,
                  onChanged: line.isSelected
                      ? (v) => onTaxIncludedChanged(v ?? false)
                      : null,
                  contentPadding: EdgeInsets.zero,
                ),
                CheckboxListTile(
                  key: purchaseTaxIncludedCheckbox(line.purchaseOrderLineId),
                  title: const Text('Tax Included in Purchase Cost'),
                  value: line.purchaseTaxIncluded,
                  onChanged: line.isSelected
                      ? (v) => onPurchaseTaxIncludedChanged(v ?? false)
                      : null,
                  contentPadding: EdgeInsets.zero,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
