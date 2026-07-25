import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';

class PurchaseOrderReceiveLineCard extends StatefulWidget {
  const PurchaseOrderReceiveLineCard({
    required this.line,
    required this.onSelectionChanged,
    required this.onQuantityChanged,
    required this.onBarcodeChanged,
    required this.onScanBarcode,
    required this.onGenerateBarcode,
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
    required this.isBarcodeGenerating,
    required this.barcodeGenerationFailure,
    this.isPrefillLoading = false,
    this.prefillFailure,
    super.key,
  });

  static Key barcodeField(String lineId) => Key('receive-line-barcode-$lineId');
  static Key scanBarcodeButton(String lineId) =>
      Key('receive-line-scan-barcode-$lineId');
  static Key generateBarcodeButton(String lineId) =>
      Key('receive-line-generate-barcode-$lineId');
  static Key batchField(String lineId) => Key('receive-line-batch-$lineId');
  static Key selectionCheckbox(String lineId) =>
      Key('receive-line-selection-$lineId');
  static Key selectionSemantics(String lineId) =>
      Key('receive-line-selection-semantics-$lineId');
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
  final VoidCallback onScanBarcode;
  final Future<void> Function() onGenerateBarcode;
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
  final bool isBarcodeGenerating;
  final String? barcodeGenerationFailure;
  final bool isPrefillLoading;
  final String? prefillFailure;

  static Key cardKey(String lineId) => Key('receive-line-card-$lineId');

  @override
  State<PurchaseOrderReceiveLineCard> createState() =>
      _PurchaseOrderReceiveLineCardState();
}

class _PurchaseOrderReceiveLineCardState
    extends State<PurchaseOrderReceiveLineCard> {
  late final TextEditingController _barcodeController;

  @override
  void initState() {
    super.initState();
    _barcodeController = TextEditingController(text: widget.line.barcode);
  }

  @override
  void dispose() {
    _barcodeController.dispose();
    super.dispose();
  }

  @override
  void didUpdateWidget(PurchaseOrderReceiveLineCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.line.barcode != _barcodeController.text) {
      _barcodeController.text = widget.line.barcode;
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      key: PurchaseOrderReceiveLineCard.cardKey(
        widget.line.purchaseOrderLineId,
      ),
      margin: const EdgeInsets.only(bottom: 12),
      child: ExpansionTile(
        key: ValueKey(
          '${widget.line.purchaseOrderLineId}-${widget.isExpanded}',
        ),
        initiallyExpanded: widget.isExpanded,
        leading: Semantics(
          key: PurchaseOrderReceiveLineCard.selectionSemantics(
            widget.line.purchaseOrderLineId,
          ),
          label: widget.line.description,
          checked: widget.line.isSelected,
          child: Checkbox(
            key: PurchaseOrderReceiveLineCard.selectionCheckbox(
              widget.line.purchaseOrderLineId,
            ),
            value: widget.line.isSelected,
            onChanged: (value) => widget.onSelectionChanged(value ?? false),
          ),
        ),
        title: Row(
          children: [
            Expanded(
              child: Text(
                widget.line.description,
                style: Theme.of(context).textTheme.titleSmall,
              ),
            ),
            if (widget.isPrefillLoading)
              const SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            if (widget.prefillFailure != null && !widget.isPrefillLoading)
              Tooltip(
                message: widget.prefillFailure,
                child: Icon(
                  Icons.warning_outlined,
                  size: 20,
                  color: Theme.of(context).colorScheme.error,
                ),
              ),
          ],
        ),
        subtitle: Text(
          '${l10n.purchaseOrderReceiveRemaining}: ${widget.line.remainingQuantity}',
        ),
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.quantityField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveLineQuantity,
                    helperText:
                        '${l10n.purchaseOrderReceiveRemaining}: ${widget.line.remainingQuantity}',
                    errorText: widget.errors['quantity'],
                  ),
                  initialValue: widget.line.quantity.toString(),
                  enabled: widget.line.isSelected,
                  keyboardType: TextInputType.number,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  onChanged: widget.onQuantityChanged,
                ),
                const SizedBox(height: 12),
                TextField(
                  key: PurchaseOrderReceiveLineCard.barcodeField(
                    widget.line.purchaseOrderLineId,
                  ),
                  controller: _barcodeController,
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveBarcodeLabel,
                    errorText:
                        widget.errors['barcode'] ??
                        widget.barcodeGenerationFailure,
                  ),
                  enabled: widget.line.isSelected,
                  onChanged: widget.onBarcodeChanged,
                  maxLength: 120,
                ),
                const SizedBox(height: 12),
                LayoutBuilder(
                  builder: (context, constraints) {
                    final stackActions =
                        constraints.maxWidth < 360 ||
                        MediaQuery.textScalerOf(context).scale(14) > 18;
                    final scanButton = Tooltip(
                      message: l10n.purchaseOrderReceiveScanBarcode,
                      child: OutlinedButton.icon(
                        key: PurchaseOrderReceiveLineCard.scanBarcodeButton(
                          widget.line.purchaseOrderLineId,
                        ),
                        icon: const Icon(Icons.document_scanner_outlined),
                        onPressed:
                            !widget.line.isSelected ||
                                widget.isBarcodeGenerating
                            ? null
                            : widget.onScanBarcode,
                        label: Text(l10n.purchaseOrderReceiveScanBarcode),
                      ),
                    );
                    final generateButton = Tooltip(
                      message: l10n.purchaseOrderReceiveGenerateBarcode,
                      child: FilledButton.icon(
                        key: PurchaseOrderReceiveLineCard.generateBarcodeButton(
                          widget.line.purchaseOrderLineId,
                        ),
                        icon: widget.isBarcodeGenerating
                            ? const SizedBox(
                                width: 16,
                                height: 16,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.auto_fix_high),
                        onPressed:
                            !widget.line.isSelected ||
                                widget.isBarcodeGenerating
                            ? null
                            : () => unawaited(widget.onGenerateBarcode()),
                        label: Text(l10n.purchaseOrderReceiveGenerateBarcode),
                      ),
                    );
                    if (stackActions) {
                      return Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          scanButton,
                          const SizedBox(height: 8),
                          generateButton,
                        ],
                      );
                    }
                    return Row(
                      children: [
                        Expanded(child: scanButton),
                        const SizedBox(width: 8),
                        Expanded(child: generateButton),
                      ],
                    );
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.batchField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveBatchLabel,
                    errorText: widget.errors['batchNumber'],
                  ),
                  initialValue: widget.line.batchNumber,
                  enabled: widget.line.isSelected,
                  onChanged: widget.onBatchNumberChanged,
                  maxLength: 80,
                ),
                const SizedBox(height: 16),
                Text(
                  l10n.purchaseOrderReceiveInventoryDetails,
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.unitCostField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveUnitCostLabel,
                    errorText: widget.errors['unitCost'],
                  ),
                  initialValue: widget.line.unitPurchaseCost.toString(),
                  enabled: widget.line.isSelected,
                  autofocus: widget.focusedField == 'unitCost',
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  inputFormatters: [_decimalFormatter],
                  onChanged: widget.onUnitPurchaseCostChanged,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.totalCostField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveTotalPurchaseCostLabel,
                    errorText: widget.errors['totalPurchaseCost'],
                  ),
                  initialValue: widget.line.totalPurchaseCost.toString(),
                  enabled: widget.line.isSelected,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  autofocus: widget.focusedField == 'totalPurchaseCost',
                  inputFormatters: [_decimalFormatter],
                  onChanged: widget.onTotalPurchaseCostChanged,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.mrpField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveMrpLabel,
                    errorText: widget.errors['mrp'],
                  ),
                  initialValue: widget.line.mrp.toString(),
                  enabled: widget.line.isSelected,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  autofocus: widget.focusedField == 'mrp',
                  inputFormatters: [_decimalFormatter],
                  onChanged: widget.onMrpChanged,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.salesPriceField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveSalesPriceLabel,
                    errorText: widget.errors['salesPrice'],
                  ),
                  initialValue: widget.line.salesPrice.toString(),
                  enabled: widget.line.isSelected,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  autofocus: widget.focusedField == 'salesPrice',
                  inputFormatters: [_decimalFormatter],
                  onChanged: widget.onSalesPriceChanged,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: PurchaseOrderReceiveLineCard.taxRateField(
                    widget.line.purchaseOrderLineId,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.purchaseOrderReceiveTaxRateLabel,
                    errorText: widget.errors['taxRatePercent'],
                  ),
                  initialValue: widget.line.taxRatePercent.toString(),
                  enabled: widget.line.isSelected,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  autofocus: widget.focusedField == 'taxRatePercent',
                  inputFormatters: [_decimalFormatter],
                  onChanged: widget.onTaxRatePercentChanged,
                ),
                const SizedBox(height: 12),
                CheckboxListTile(
                  key: PurchaseOrderReceiveLineCard.taxIncludedCheckbox(
                    widget.line.purchaseOrderLineId,
                  ),
                  title: Text(l10n.purchaseOrderReceiveTaxIncludedLabel),
                  subtitle: widget.errors['taxIncluded'] == null
                      ? null
                      : Text(
                          widget.errors['taxIncluded']!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                  value: widget.line.taxIncluded,
                  onChanged: widget.line.isSelected
                      ? (v) => widget.onTaxIncludedChanged(v ?? false)
                      : null,
                  contentPadding: EdgeInsets.zero,
                ),
                CheckboxListTile(
                  key: PurchaseOrderReceiveLineCard.purchaseTaxIncludedCheckbox(
                    widget.line.purchaseOrderLineId,
                  ),
                  title: Text(
                    l10n.purchaseOrderReceivePurchaseTaxIncludedLabel,
                  ),
                  subtitle: widget.errors['purchaseTaxIncluded'] == null
                      ? null
                      : Text(
                          widget.errors['purchaseTaxIncluded']!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                  value: widget.line.purchaseTaxIncluded,
                  onChanged: widget.line.isSelected
                      ? (v) => widget.onPurchaseTaxIncludedChanged(v ?? false)
                      : null,
                  contentPadding: EdgeInsets.zero,
                ),
                _ReceiptDateField(
                  fieldKey: PurchaseOrderReceiveLineCard.manufacturingDateField(
                    widget.line.purchaseOrderLineId,
                  ),
                  label: l10n.purchaseOrderReceiveManufacturingDateLabel,
                  value: widget.line.manufacturingDate,
                  errorText: widget.errors['manufacturingDate'],
                  enabled: widget.line.isSelected,
                  autofocus: widget.focusedField == 'manufacturingDate',
                  clearKey: PurchaseOrderReceiveLineCard.manufacturingDateClear(
                    widget.line.purchaseOrderLineId,
                  ),
                  onChanged: widget.onManufacturingDateChanged,
                ),
                const SizedBox(height: 12),
                _ReceiptDateField(
                  fieldKey: PurchaseOrderReceiveLineCard.expiryDateField(
                    widget.line.purchaseOrderLineId,
                  ),
                  label: l10n.purchaseOrderReceiveExpiryDateLabel,
                  value: widget.line.expiryDate,
                  errorText: widget.errors['expiryDate'],
                  enabled: widget.line.isSelected,
                  autofocus: widget.focusedField == 'expiryDate',
                  clearKey: PurchaseOrderReceiveLineCard.expiryDateClear(
                    widget.line.purchaseOrderLineId,
                  ),
                  onChanged: widget.onExpiryDateChanged,
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

class _ReceiptDateField extends StatefulWidget {
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
  State<_ReceiptDateField> createState() => _ReceiptDateFieldState();
}

class _ReceiptDateFieldState extends State<_ReceiptDateField> {
  late final TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController();
    _scheduleControllerSync();
  }

  @override
  void didUpdateWidget(_ReceiptDateField oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.value != oldWidget.value) {
      _scheduleControllerSync();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _scheduleControllerSync() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _syncController();
    });
  }

  void _syncController() {
    final text = widget.value == null
        ? ''
        : MaterialLocalizations.of(context).formatMediumDate(widget.value!);
    if (_controller.text != text) {
      _controller.text = text;
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return TextFormField(
      key: widget.fieldKey,
      controller: _controller,
      readOnly: true,
      enabled: widget.enabled,
      autofocus: widget.autofocus,
      decoration: InputDecoration(
        labelText: widget.label,
        hintText: l10n.purchaseOrderReceiveSelectDate,
        errorText: widget.errorText,
        suffixIcon: widget.value == null
            ? const Icon(Icons.calendar_month)
            : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  IconButton(
                    key: widget.clearKey,
                    tooltip: l10n.purchaseOrderReceiveClearDate,
                    onPressed: widget.enabled
                        ? () => widget.onChanged(null)
                        : null,
                    icon: const Icon(Icons.clear),
                  ),
                  const Icon(Icons.calendar_month),
                ],
              ),
      ),
      onTap: widget.enabled ? () => _pickDate(context) : null,
    );
  }

  Future<void> _pickDate(BuildContext context) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: widget.value ?? now,
      firstDate: DateTime(now.year - 100),
      lastDate: DateTime(now.year + 100),
    );
    if (picked != null) widget.onChanged(picked);
  }
}
