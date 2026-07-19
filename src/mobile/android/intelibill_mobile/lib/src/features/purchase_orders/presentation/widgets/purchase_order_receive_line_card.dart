import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';
import 'package:intl/intl.dart';

class PurchaseOrderReceiveLineCard extends StatelessWidget {
  const PurchaseOrderReceiveLineCard({
    required this.line,
    required this.onSelectionChanged,
    required this.onQuantityChanged,
    required this.onBarcodeChanged,
    required this.onBatchNumberChanged,
    required this.onUnitPurchaseCostChanged,
    required this.onTotalPurchaseCostChanged,
    required this.onMrpChanged,
    required this.onSalesPriceChanged,
    required this.onTaxRatePercentChanged,
    required this.onTaxIncludedChanged,
    required this.onPurchaseTaxIncludedChanged,
    required this.onExpiryDateChanged,
    required this.onManufacturingDateChanged,
    required this.isExpanded,
    required this.focusedField,
    required this.errors,
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
  static Key unitCostField(String lineId) =>
      Key('receive-line-unit-cost-$lineId');
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
  static Key expiryDateClear(String lineId) =>
      Key('receive-line-expiry-date-clear-$lineId');
  static Key manufacturingDateClear(String lineId) =>
      Key('receive-line-manufacturing-date-clear-$lineId');

  final ReceivePurchaseOrderLineDraft line;
  final ValueChanged<bool> onSelectionChanged;
  final ValueChanged<String> onQuantityChanged;
  final ValueChanged<String> onBarcodeChanged;
  final ValueChanged<String> onBatchNumberChanged;
  final ValueChanged<String> onUnitPurchaseCostChanged;
  final ValueChanged<String> onTotalPurchaseCostChanged;
  final ValueChanged<String> onMrpChanged;
  final ValueChanged<String> onSalesPriceChanged;
  final ValueChanged<String> onTaxRatePercentChanged;
  final ValueChanged<bool> onTaxIncludedChanged;
  final ValueChanged<bool> onPurchaseTaxIncludedChanged;
  final ValueChanged<DateTime?> onExpiryDateChanged;
  final ValueChanged<DateTime?> onManufacturingDateChanged;
  final bool isExpanded;
  final String? focusedField;
  final Map<String, String> errors;

  static Key cardKey(String lineId) => Key('receive-line-card-$lineId');

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      key: cardKey(line.purchaseOrderLineId),
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ExpansionTile(
        key: ValueKey('${line.purchaseOrderLineId}-$isExpanded'),
        initiallyExpanded: isExpanded,
        leading: Checkbox(
          key: selectionCheckbox(line.purchaseOrderLineId),
          value: line.isSelected,
          onChanged: (value) => onSelectionChanged(value ?? false),
        ),
        title: Text(
          line.description,
          style: Theme.of(context).textTheme.titleSmall,
        ),
        subtitle: Text(
          '${l10n.purchaseOrderReceiveRemaining}: ${line.remainingQuantity}',
        ),
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
                    helperText:
                        '${l10n.purchaseOrderReceiveRemaining}: ${line.remainingQuantity}',
                    errorText: errors['quantity'],
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
                    errorText: errors['barcode'],
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
                    errorText: errors['batchNumber'],
                  ),
                  initialValue: line.batchNumber,
                  enabled: line.isSelected,
                  onChanged: onBatchNumberChanged,
                  maxLength: 80,
                ),
                const SizedBox(height: 16),
                Text(
                  l10n.purchaseOrderReceiveInventoryDetails,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: unitCostField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveUnitCostLabel,
                    errorText: errors['unitCost'],
                  ),
                  initialValue: line.unitPurchaseCost.toString(),
                  enabled: line.isSelected,
                  autofocus: focusedField == 'unitCost',
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  inputFormatters: [_decimalFormatter],
                  onChanged: onUnitPurchaseCostChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: totalCostField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveTotalPurchaseCostLabel,
                    errorText: errors['totalPurchaseCost'],
                  ),
                  initialValue: line.totalPurchaseCost.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  autofocus: focusedField == 'totalPurchaseCost',
                  inputFormatters: [_decimalFormatter],
                  onChanged: onTotalPurchaseCostChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: mrpField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveMrpLabel,
                    errorText: errors['mrp'],
                  ),
                  initialValue: line.mrp.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  autofocus: focusedField == 'mrp',
                  inputFormatters: [_decimalFormatter],
                  onChanged: onMrpChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: salesPriceField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveSalesPriceLabel,
                    errorText: errors['salesPrice'],
                  ),
                  initialValue: line.salesPrice.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  autofocus: focusedField == 'salesPrice',
                  inputFormatters: [_decimalFormatter],
                  onChanged: onSalesPriceChanged,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  key: taxRateField(line.purchaseOrderLineId),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveTaxRateLabel,
                    errorText: errors['taxRatePercent'],
                  ),
                  initialValue: line.taxRatePercent.toString(),
                  enabled: line.isSelected,
                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                  autofocus: focusedField == 'taxRatePercent',
                  inputFormatters: [_decimalFormatter],
                  onChanged: onTaxRatePercentChanged,
                ),
                const SizedBox(height: 12),
                CheckboxListTile(
                  key: taxIncludedCheckbox(line.purchaseOrderLineId),
                  title: Text(l10n.purchaseOrderReceiveTaxIncludedLabel),
                  subtitle: errors['taxIncluded'] == null
                      ? null
                      : Text(errors['taxIncluded']!),
                  value: line.taxIncluded,
                  onChanged: line.isSelected
                      ? (v) => onTaxIncludedChanged(v ?? false)
                      : null,
                  contentPadding: EdgeInsets.zero,
                ),
                CheckboxListTile(
                  key: purchaseTaxIncludedCheckbox(line.purchaseOrderLineId),
                  title: Text(
                    l10n.purchaseOrderReceivePurchaseTaxIncludedLabel,
                  ),
                  subtitle: errors['purchaseTaxIncluded'] == null
                      ? null
                      : Text(errors['purchaseTaxIncluded']!),
                  value: line.purchaseTaxIncluded,
                  onChanged: line.isSelected
                      ? (v) => onPurchaseTaxIncludedChanged(v ?? false)
                      : null,
                  contentPadding: EdgeInsets.zero,
                ),
                _ReceiptDateField(
                  fieldKey: manufacturingDateField(line.purchaseOrderLineId),
                  label: l10n.purchaseOrderReceiveManufacturingDateLabel,
                  value: line.manufacturingDate,
                  errorText: errors['manufacturingDate'],
                  enabled: line.isSelected,
                  autofocus: focusedField == 'manufacturingDate',
                  clearKey: manufacturingDateClear(line.purchaseOrderLineId),
                  onChanged: onManufacturingDateChanged,
                ),
                const SizedBox(height: 8),
                _ReceiptDateField(
                  fieldKey: expiryDateField(line.purchaseOrderLineId),
                  label: l10n.purchaseOrderReceiveExpiryDateLabel,
                  value: line.expiryDate,
                  errorText: errors['expiryDate'],
                  enabled: line.isSelected,
                  autofocus: focusedField == 'expiryDate',
                  clearKey: expiryDateClear(line.purchaseOrderLineId),
                  onChanged: onExpiryDateChanged,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  static final _decimalFormatter = FilteringTextInputFormatter.allow(
    RegExp(r'^-?\d*\.?\d{0,2}'),
  );
}

class _ReceiptDateField extends StatelessWidget {
  const _ReceiptDateField({
    required this.fieldKey,
    required this.label,
    required this.value,
    required this.errorText,
    required this.enabled,
    required this.autofocus,
    required this.clearKey,
    required this.onChanged,
  });

  final Key fieldKey;
  final String label;
  final DateTime? value;
  final String? errorText;
  final bool enabled;
  final bool autofocus;
  final Key clearKey;
  final ValueChanged<DateTime?> onChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return InputDecorator(
      decoration: InputDecoration(labelText: label, errorText: errorText),
      child: Row(
        children: [
          Expanded(
            child: OutlinedButton(
              key: fieldKey,
              autofocus: autofocus,
              onPressed: !enabled ? null : () => _pickDate(context),
              child: Text(
                value == null
                    ? l10n.purchaseOrderReceiveSelectDate
                    : DateFormat.yMMMd().format(value!),
              ),
            ),
          ),
          if (value != null)
            IconButton(
              key: clearKey,
              tooltip: l10n.purchaseOrderReceiveClearDate,
              onPressed: enabled ? () => onChanged(null) : null,
              icon: const Icon(Icons.clear),
            ),
        ],
      ),
    );
  }

  Future<void> _pickDate(BuildContext context) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: value ?? now,
      firstDate: DateTime(now.year - 100),
      lastDate: DateTime(now.year + 100),
    );
    if (picked != null) onChanged(picked);
  }
}
