import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/add_inventory_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/show_barcode_scanner.dart';

class AddInventoryPage extends ConsumerStatefulWidget {
  const AddInventoryPage({super.key});

  static const itemNameFieldKey = Key('add-inventory-item-name');
  static const barcodeFieldKey = Key('add-inventory-barcode');
  static const scanBarcodeButtonKey = Key('add-inventory-scan-barcode');
  static const uomFieldKey = Key('add-inventory-uom');
  static const batchNumberFieldKey = Key('add-inventory-batch-number');
  static const quantityFieldKey = Key('add-inventory-quantity');
  static const costPriceFieldKey = Key('add-inventory-cost-price');
  static const mrpFieldKey = Key('add-inventory-mrp');
  static const salesPriceFieldKey = Key('add-inventory-sales-price');
  static const taxRateFieldKey = Key('add-inventory-tax-rate');
  static const taxIncludedSwitchKey = Key('add-inventory-tax-included');
  static const expiryDateFieldKey = Key('add-inventory-expiry-date');
  static const manufacturingDateFieldKey = Key(
    'add-inventory-manufacturing-date',
  );
  static const referenceFieldKey = Key('add-inventory-reference');
  static const notesFieldKey = Key('add-inventory-notes');
  static const submitButtonKey = Key('add-inventory-submit');

  @override
  ConsumerState<AddInventoryPage> createState() => _AddInventoryPageState();
}

class _AddInventoryPageState extends ConsumerState<AddInventoryPage> {
  final _formKey = GlobalKey<FormState>();
  final _itemNameController = TextEditingController();
  final _barcodeController = TextEditingController();
  final _uomController = TextEditingController();
  final _batchNumberController = TextEditingController();
  final _quantityController = TextEditingController();
  final _costPriceController = TextEditingController();
  final _mrpController = TextEditingController();
  final _salesPriceController = TextEditingController();
  final _taxRateController = TextEditingController(text: '0');
  final _expiryDateController = TextEditingController();
  final _manufacturingDateController = TextEditingController();
  final _referenceController = TextEditingController();
  final _notesController = TextEditingController();
  final _itemNameFocusNode = FocusNode();
  final _barcodeFocusNode = FocusNode();

  DateTime? _expiryDate;
  DateTime? _manufacturingDate;
  bool _taxIncluded = false;

  @override
  void initState() {
    super.initState();
    _batchNumberController.text = _generateBatchNumber();
    _barcodeFocusNode.addListener(_handleBarcodeFocusChange);
  }

  @override
  void dispose() {
    _barcodeFocusNode.removeListener(_handleBarcodeFocusChange);
    _itemNameController.dispose();
    _barcodeController.dispose();
    _uomController.dispose();
    _batchNumberController.dispose();
    _quantityController.dispose();
    _costPriceController.dispose();
    _mrpController.dispose();
    _salesPriceController.dispose();
    _taxRateController.dispose();
    _expiryDateController.dispose();
    _manufacturingDateController.dispose();
    _referenceController.dispose();
    _notesController.dispose();
    _itemNameFocusNode.dispose();
    _barcodeFocusNode.dispose();
    super.dispose();
  }

  void _handleBarcodeFocusChange() {
    if (!_barcodeFocusNode.hasFocus) {
      unawaited(_fetchAndFillProductDetails());
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(addInventoryControllerProvider, (previous, next) {
      final shouldClose =
          previous?.lastInboundSucceeded != true && next.lastInboundSucceeded;
      if (shouldClose &&
          mounted &&
          Navigator.of(context).canPop() &&
          (ModalRoute.of(context)?.isCurrent ?? false)) {
        Navigator.of(context).pop(true);
      }
    });

    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(addInventoryControllerProvider);
    final itemCatalog = ref.watch(itemsControllerProvider).items;
    final theme = Theme.of(context);
    final isSubmitting = state.isSubmitting;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellBatchInventoryInbound),
        leading: const BackButton(),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: SafeArea(
        child: Form(
          key: _formKey,
          autovalidateMode: AutovalidateMode.onUserInteraction,
          child: SingleChildScrollView(
            padding: EdgeInsets.fromLTRB(
              20,
              16,
              20,
              24 + MediaQuery.of(context).viewInsets.bottom,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _SectionTitle(title: l10n.inventoryInboundSectionProduct),
                const SizedBox(height: 12),
                RawAutocomplete<Item>(
                  textEditingController: _itemNameController,
                  focusNode: _itemNameFocusNode,
                  displayStringForOption: (item) => item.name,
                  optionsBuilder: (textEditingValue) =>
                      _matchingItems(itemCatalog, textEditingValue.text),
                  onSelected: _fillProductFromItem,
                  fieldViewBuilder:
                      (context, controller, focusNode, onFieldSubmitted) {
                        return TextFormField(
                          key: AddInventoryPage.itemNameFieldKey,
                          controller: controller,
                          focusNode: focusNode,
                          enabled: !isSubmitting,
                          textInputAction: TextInputAction.next,
                          decoration: InputDecoration(
                            labelText: l10n.inventoryInboundItemNameLabel,
                            border: const OutlineInputBorder(),
                          ),
                          validator: (value) => _validateRequired(
                            value,
                            requiredMessage:
                                l10n.inventoryInboundItemNameRequired,
                            maxMessage: l10n.inventoryInboundItemNameMax,
                            maxLength: 200,
                          ),
                          onFieldSubmitted: (_) => onFieldSubmitted(),
                        );
                      },
                  optionsViewBuilder: (context, onSelected, options) =>
                      _InventoryItemOptions(
                        options: options.toList(growable: false),
                        onSelected: onSelected,
                        displayValue: (item) => item.name,
                      ),
                ),
                const SizedBox(height: 12),
                RawAutocomplete<Item>(
                  textEditingController: _barcodeController,
                  focusNode: _barcodeFocusNode,
                  displayStringForOption: (item) => item.barcode,
                  optionsBuilder: (textEditingValue) =>
                      _matchingItems(itemCatalog, textEditingValue.text),
                  onSelected: _fillProductFromItem,
                  fieldViewBuilder:
                      (context, controller, focusNode, onFieldSubmitted) {
                        return TextFormField(
                          key: AddInventoryPage.barcodeFieldKey,
                          controller: controller,
                          focusNode: focusNode,
                          enabled: !isSubmitting,
                          textInputAction: TextInputAction.next,
                          decoration: InputDecoration(
                            labelText: l10n.inventoryInboundBarcodeLabel,
                            border: const OutlineInputBorder(),
                            suffixIcon: IconButton(
                              key: AddInventoryPage.scanBarcodeButtonKey,
                              icon: const Icon(Icons.qr_code_scanner),
                              onPressed: isSubmitting ? null : _scanBarcode,
                            ),
                          ),
                          validator: (value) => _validateRequired(
                            value,
                            requiredMessage:
                                l10n.inventoryInboundBarcodeRequired,
                            maxMessage: l10n.inventoryInboundBarcodeMax,
                            maxLength: 120,
                          ),
                          onFieldSubmitted: (_) => onFieldSubmitted(),
                        );
                      },
                  optionsViewBuilder: (context, onSelected, options) =>
                      _InventoryItemOptions(
                        options: options.toList(growable: false),
                        onSelected: onSelected,
                        displayValue: (item) => item.barcode,
                      ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.uomFieldKey,
                  controller: _uomController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundUomLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateRequired(
                    value,
                    requiredMessage: l10n.inventoryInboundUomRequired,
                    maxMessage: l10n.inventoryInboundUomMax,
                    maxLength: 40,
                  ),
                ),
                const SizedBox(height: 20),
                _SectionTitle(title: l10n.inventoryInboundSectionBatch),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.batchNumberFieldKey,
                  controller: _batchNumberController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundBatchNumberLabel,
                    hintText: l10n.inventoryInboundBatchNumberHint,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateOptional(
                    value,
                    maxMessage: l10n.inventoryInboundBatchNumberMax,
                    maxLength: 80,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.quantityFieldKey,
                  controller: _quantityController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundQuantityLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateQuantity(value, l10n),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.costPriceFieldKey,
                  controller: _costPriceController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundCostPriceLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateRequiredNumber(
                    value,
                    requiredMessage: l10n.inventoryInboundCostPriceRequired,
                    invalidMessage: l10n.inventoryInboundNumberInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.mrpFieldKey,
                  controller: _mrpController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundMrpLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateRequiredNumber(
                    value,
                    requiredMessage: l10n.inventoryInboundMrpRequired,
                    invalidMessage: l10n.inventoryInboundNumberInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.salesPriceFieldKey,
                  controller: _salesPriceController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundSalesPriceLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateRequiredNumber(
                    value,
                    requiredMessage: l10n.inventoryInboundSalesPriceRequired,
                    invalidMessage: l10n.inventoryInboundNumberInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.taxRateFieldKey,
                  controller: _taxRateController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundTaxRateLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateTaxRate(value, l10n),
                ),
                SwitchListTile(
                  key: AddInventoryPage.taxIncludedSwitchKey,
                  value: _taxIncluded,
                  onChanged: isSubmitting
                      ? null
                      : (value) {
                          setState(() {
                            _taxIncluded = value;
                          });
                        },
                  title: Text(l10n.inventoryInboundTaxIncludedLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 8),
                _SectionTitle(title: l10n.inventoryInboundSectionAdditional),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.expiryDateFieldKey,
                  controller: _expiryDateController,
                  enabled: !isSubmitting,
                  readOnly: true,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundExpiryDateLabel,
                    border: const OutlineInputBorder(),
                    suffixIcon: const Icon(Icons.calendar_month),
                  ),
                  onTap: isSubmitting
                      ? null
                      : () => _pickDate(
                          currentValue: _expiryDate,
                          onSelected: (date) {
                            setState(() {
                              _expiryDate = date;
                            });
                          },
                          controller: _expiryDateController,
                        ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.manufacturingDateFieldKey,
                  controller: _manufacturingDateController,
                  enabled: !isSubmitting,
                  readOnly: true,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundManufacturingDateLabel,
                    border: const OutlineInputBorder(),
                    suffixIcon: const Icon(Icons.calendar_month),
                  ),
                  onTap: isSubmitting
                      ? null
                      : () => _pickDate(
                          currentValue: _manufacturingDate,
                          onSelected: (date) {
                            setState(() {
                              _manufacturingDate = date;
                            });
                          },
                          controller: _manufacturingDateController,
                        ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.referenceFieldKey,
                  controller: _referenceController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundReferenceLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateOptional(
                    value,
                    maxMessage: l10n.inventoryInboundReferenceMax,
                    maxLength: 80,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddInventoryPage.notesFieldKey,
                  controller: _notesController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.newline,
                  maxLines: 4,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryInboundNotesLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateOptional(
                    value,
                    maxMessage: l10n.inventoryInboundNotesMax,
                    maxLength: 500,
                  ),
                ),
                const SizedBox(height: 20),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton(
                    key: AddInventoryPage.submitButtonKey,
                    onPressed: isSubmitting ? null : _submit,
                    child: isSubmitting
                        ? const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              color: Colors.white,
                            ),
                          )
                        : Text(l10n.inventoryMenuAddInventory),
                  ),
                ),
                if (state.submitFailure != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    _localizeSubmitFailure(l10n, state.submitFailure!),
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: theme.colorScheme.error,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }

  Iterable<Item> _matchingItems(List<Item> items, String query) {
    final normalized = query.trim().toLowerCase();
    if (normalized.isEmpty) {
      return const Iterable<Item>.empty();
    }

    return items
        .where((item) {
          return item.isActive &&
              (item.name.toLowerCase().contains(normalized) ||
                  item.barcode.toLowerCase().contains(normalized));
        })
        .take(8);
  }

  void _fillProductFromItem(Item item) {
    _itemNameController.text = item.name;
    _barcodeController.text = item.barcode;
    if (_uomController.text.trim().isEmpty) {
      _uomController.text = item.uom;
    }
    unawaited(_fetchAndFillProductDetails());
  }

  Future<void> _scanBarcode() async {
    final result = await showBarcodeScanner(context);
    if (result == null || result.value.isEmpty) return;

    if (!mounted) return;

    setState(() {
      _barcodeController.text = result.value;

      final itemCatalog = ref.read(itemsControllerProvider).items;
      final matchedItem = itemCatalog
          .where((item) => item.isActive && item.barcode == result.value)
          .firstOrNull;

      if (matchedItem != null) {
        _itemNameController.text = matchedItem.name;
        if (_uomController.text.trim().isEmpty) {
          _uomController.text = matchedItem.uom;
        }
      }
    });

    unawaited(_fetchAndFillProductDetails());
  }

  Future<void> _fetchAndFillProductDetails() async {
    final itemName = _itemNameController.text.trim();
    final barcode = _barcodeController.text.trim();
    if (itemName.isEmpty && barcode.isEmpty) {
      return;
    }

    try {
      final details = await ref
          .read(getProductDetailsProvider)
          .call(
            name: itemName.isEmpty ? null : itemName,
            barcode: barcode.isEmpty ? null : barcode,
          );
      if (!mounted) return;
      setState(() {
        _fillProductDetails(details);
      });
    } on AppException {
      // Product details are a convenience fill; keep manual entry usable.
    }
  }

  void _fillProductDetails(ProductDetails details) {
    if (_itemNameController.text.trim().isEmpty && details.name.isNotEmpty) {
      _itemNameController.text = details.name;
    }
    if (_uomController.text.trim().isEmpty && details.uom.isNotEmpty) {
      _uomController.text = details.uom;
    }
    _setIfEmpty(_costPriceController, details.costPrice);
    _setIfEmpty(_mrpController, details.mrp);
    _setIfEmpty(_salesPriceController, details.salesPrice);

    if (_taxRateController.text.trim().isEmpty ||
        _taxRateController.text.trim() == '0') {
      _taxRateController.text = _formatNumber(details.taxRatePercent ?? 0);
    }

    if (details.taxIncluded != null) {
      _taxIncluded = details.taxIncluded!;
    }
  }

  void _setIfEmpty(TextEditingController controller, double value) {
    if (controller.text.trim().isEmpty) {
      controller.text = _formatNumber(value);
    }
  }

  String _formatNumber(double value) {
    if (value == value.roundToDouble()) {
      return value.toInt().toString();
    }
    return value.toString();
  }

  String _generateBatchNumber() {
    final now = DateTime.now();
    final date =
        '${now.year.toString().padLeft(4, '0')}'
        '${now.month.toString().padLeft(2, '0')}'
        '${now.day.toString().padLeft(2, '0')}';
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    final random = now.microsecondsSinceEpoch;
    final suffix = StringBuffer();
    for (var i = 0; i < 5; i++) {
      suffix.write(chars[(random + (i * 17)) % chars.length]);
    }
    return 'BN-$date-$suffix';
  }

  Future<void> _pickDate({
    required DateTime? currentValue,
    required ValueChanged<DateTime> onSelected,
    required TextEditingController controller,
  }) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: currentValue ?? now,
      firstDate: DateTime(now.year - 20),
      lastDate: DateTime(now.year + 30),
    );
    if (!mounted || picked == null) return;
    onSelected(picked);
    controller.text = MaterialLocalizations.of(
      context,
    ).formatMediumDate(picked);
  }

  String? _validateRequired(
    String? value, {
    required String requiredMessage,
    required String maxMessage,
    required int maxLength,
  }) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return requiredMessage;
    }
    if (trimmed.length > maxLength) {
      return maxMessage;
    }
    return null;
  }

  String? _validateOptional(
    String? value, {
    required String maxMessage,
    required int maxLength,
  }) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return null;
    }
    if (trimmed.length > maxLength) {
      return maxMessage;
    }
    return null;
  }

  String? _validateRequiredNumber(
    String? value, {
    required String requiredMessage,
    required String invalidMessage,
  }) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return requiredMessage;
    }
    final parsed = double.tryParse(trimmed);
    if (parsed == null) {
      return invalidMessage;
    }
    return null;
  }

  String? _validateQuantity(String? value, AppLocalizations l10n) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return l10n.inventoryInboundQuantityRequired;
    }
    final parsed = double.tryParse(trimmed);
    if (parsed == null) {
      return l10n.inventoryInboundNumberInvalid;
    }
    if (parsed <= 0) {
      return l10n.inventoryInboundQuantityMin;
    }
    return null;
  }

  String? _validateTaxRate(String? value, AppLocalizations l10n) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return null;
    }
    final parsed = double.tryParse(trimmed);
    if (parsed == null) {
      return l10n.inventoryInboundNumberInvalid;
    }
    if (parsed < 0 || parsed > 100) {
      return l10n.inventoryInboundTaxRateRange;
    }
    return null;
  }

  void _submit() {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    String? optionalValue(TextEditingController controller) {
      final trimmed = controller.text.trim();
      return trimmed.isEmpty ? null : trimmed;
    }

    final taxRateText = _taxRateController.text.trim();
    final taxRate = taxRateText.isEmpty ? 0.0 : double.parse(taxRateText);

    unawaited(
      ref
          .read(addInventoryControllerProvider.notifier)
          .submitInbound(
            itemName: _itemNameController.text.trim(),
            barcode: _barcodeController.text.trim(),
            uom: _uomController.text.trim(),
            batchNumber: optionalValue(_batchNumberController),
            quantity: double.parse(_quantityController.text.trim()),
            costPrice: double.parse(_costPriceController.text.trim()),
            mrp: double.parse(_mrpController.text.trim()),
            salesPrice: double.parse(_salesPriceController.text.trim()),
            taxRate: taxRate,
            taxIncluded: _taxIncluded,
            expiryDate: _expiryDate,
            manufacturingDate: _manufacturingDate,
            referenceNumber: optionalValue(_referenceController),
            notes: optionalValue(_notesController),
          ),
    );
  }
}

class _InventoryItemOptions extends StatelessWidget {
  const _InventoryItemOptions({
    required this.options,
    required this.onSelected,
    required this.displayValue,
  });

  final List<Item> options;
  final ValueChanged<Item> onSelected;
  final String Function(Item item) displayValue;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Align(
      alignment: Alignment.topLeft,
      child: Material(
        elevation: 4,
        borderRadius: BorderRadius.circular(8),
        clipBehavior: Clip.antiAlias,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxHeight: 240, maxWidth: 420),
          child: ListView.separated(
            padding: EdgeInsets.zero,
            shrinkWrap: true,
            itemCount: options.length,
            separatorBuilder: (context, index) => const Divider(height: 1),
            itemBuilder: (context, index) {
              final item = options[index];
              return InkWell(
                onTap: () => onSelected(item),
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 12,
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        displayValue(item),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.bodyLarge?.copyWith(
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        '${item.name} · ${item.barcode} · ${item.uom}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.title});

  final String title;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Text(
      title,
      style: theme.textTheme.titleMedium?.copyWith(
        fontWeight: FontWeight.w600,
      ),
    );
  }
}

String _localizeSubmitFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? errors) {
      if (errors != null && errors.isNotEmpty) {
        return errors.values.first.first;
      }
      if (message != null && message.isNotEmpty) {
        return message;
      }
      return l10n.inventoryInboundErrorGeneric;
    },
    unauthorized: (String? _) => l10n.inventoryInboundErrorGeneric,
    forbidden: (String? _) => l10n.inventoryInboundErrorGeneric,
    notFound: (String? _) => l10n.inventoryInboundErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.inventoryInboundErrorGeneric,
    network: (String? _) => l10n.inventoryInboundErrorNetwork,
    timeout: (String? _) => l10n.inventoryInboundErrorTimeout,
    serialization: (String? _) => l10n.inventoryInboundErrorGeneric,
    unknown: (String? _) => l10n.inventoryInboundErrorGeneric,
  );
}
