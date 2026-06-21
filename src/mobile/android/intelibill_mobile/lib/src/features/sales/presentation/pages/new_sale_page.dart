import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/payment_method.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/goods_cart_list.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/payment_section.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_detail_sheet.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sellable_search_results.dart';

class NewSalePage extends ConsumerStatefulWidget {
  const NewSalePage({super.key});

  static const createCustomerButtonKey = Key('new-sale-create-customer');
  static const createCustomerSubmitButtonKey = Key(
    'new-sale-create-customer-submit',
  );
  static const customerDropdownKey = Key('new-sale-customer-dropdown');
  static const paymentFailureKey = Key('new-sale-payment-failure');
  static const submitButtonKey = Key('new-sale-submit-button');

  @override
  ConsumerState<NewSalePage> createState() => _NewSalePageState();
}

class _NewSalePageState extends ConsumerState<NewSalePage> {
  late final TextEditingController _searchController;
  late final TextEditingController _barcodeController;
  late final TextEditingController _paidAmountController;
  late final TextEditingController _dueAmountController;
  late final FocusNode _paidAmountFocusNode;
  late final FocusNode _dueAmountFocusNode;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
    _barcodeController = TextEditingController();
    _paidAmountController = TextEditingController();
    _dueAmountController = TextEditingController();
    _paidAmountFocusNode = FocusNode()
      ..addListener(_handlePaidAmountFocusChange);
    _dueAmountFocusNode = FocusNode()..addListener(_handleDueAmountFocusChange);
  }

  @override
  void dispose() {
    _searchController.dispose();
    _barcodeController.dispose();
    _paidAmountController.dispose();
    _dueAmountController.dispose();
    _paidAmountFocusNode
      ..removeListener(_handlePaidAmountFocusChange)
      ..dispose();
    _dueAmountFocusNode
      ..removeListener(_handleDueAmountFocusChange)
      ..dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(newSaleControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    _syncController(_searchController, state.searchTerm);
    _syncController(_barcodeController, state.barcodeTerm);
    _syncAmountController(
      _paidAmountController,
      _paidAmountFocusNode,
      state.paidAmount,
    );
    _syncAmountController(
      _dueAmountController,
      _dueAmountFocusNode,
      state.dueAmount,
    );

    return Scaffold(
      appBar: AppBar(title: Text(l10n.shellNewSale)),
      body: SafeArea(
        child: SingleChildScrollView(
          child: Column(
            children: [
              _buildSearchSection(state),
              const Divider(height: 1),
              if (state.cartLines.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.all(12),
                  child: _buildPaymentSection(state),
                ),
              state.isSearching
                  ? const Center(child: CircularProgressIndicator())
                  : SellableSearchResults(
                      sellables: state.results,
                      onAdd: _onAddGoods,
                    ),
              const Divider(height: 1),
              GoodsCartList(
                lines: state.cartLines,
                subtotal: state.subtotalAmount,
                tax: state.taxAmount,
                discount: state.discountAmount,
                saleDiscountType: state.saleDiscountType,
                saleDiscountValue: state.saleDiscountValue,
                saleDiscountError: state.saleDiscountError,
                itemDiscountErrors: state.itemDiscountErrors,
                total: state.cartTotal,
                discountCapacity: state.discountCapacityAmount,
                preview: state.preview,
                previewFailure: state.previewFailure,
                isPreviewLoading: state.isPreviewLoading,
                canSubmitCheckout: state.canSubmitCheckout,
                onRefreshPreview: () => ref
                    .read(newSaleControllerProvider.notifier)
                    .refreshPreview(),
                isSubmitting: state.isSubmitting,
                onSubmit: _submitSale,
                recordedSale: state.recordedSale,
                onViewRecordedSale: state.recordedSale == null
                    ? null
                    : () => _showRecordedSaleDetail(
                        context,
                        state.recordedSale!,
                      ),
                onViewRecordedReceipt: state.recordedSale == null
                    ? null
                    : () => _showRecordedSaleReceipt(
                        context,
                        state.recordedSale!,
                      ),
                onClearRecordedSale: () {
                  ref
                      .read(newSaleControllerProvider.notifier)
                      .clearRecordedSale();
                },
                onSaleDiscountTypeChanged: _updateSaleDiscountType,
                onSaleDiscountValueChanged: _updateSaleDiscountValue,
                onDecrease: _decreaseQty,
                onIncrease: _increaseQty,
                onRemove: _removeFromCart,
                onCartItemDiscountTypeChanged: _updateCartItemDiscountType,
                onCartItemDiscountValueChanged: _updateCartItemDiscountValue,
                onUnitPriceChanged: _updateUnitPrice,
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildSearchSection(NewSaleState state) {
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(
        children: [
          TextField(
            key: const Key('sales-search-field'),
            controller: _searchController,
            onChanged: (value) {
              ref
                  .read(newSaleControllerProvider.notifier)
                  .updateSearchTerm(value);
            },
            decoration: const InputDecoration(
              labelText: 'Search goods',
              border: OutlineInputBorder(),
              prefixIcon: Icon(Icons.search),
            ),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: TextField(
                  key: const Key('barcode-field'),
                  controller: _barcodeController,
                  onChanged: (value) {
                    ref
                        .read(newSaleControllerProvider.notifier)
                        .updateBarcodeTerm(value);
                  },
                  decoration: const InputDecoration(
                    labelText: 'Barcode lookup',
                    border: OutlineInputBorder(),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              ElevatedButton(
                key: const Key('barcode-search-button'),
                onPressed: _searchByBarcode,
                child: const Text('Lookup'),
              ),
            ],
          ),
          const SizedBox(height: 8),
          SizedBox(
            width: double.infinity,
            child: FilledButton(
              onPressed: () =>
                  ref.read(newSaleControllerProvider.notifier).search(),
              child: const Text('Search'),
            ),
          ),
          if (state.searchFailure != null) ...[
            const SizedBox(height: 8),
            Text(
              state.searchFailure.toString(),
              key: const Key('new-sale-failure'),
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
          if (state.submitFailure != null) ...[
            const SizedBox(height: 8),
            Text(
              state.submitFailure.toString(),
              key: const Key('new-sale-submit-failure'),
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildPaymentSection(NewSaleState state) {
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          _buildCustomerSection(state),
          const SizedBox(height: 12),
          PaymentSection(
            state: state,
            paidAmountController: _paidAmountController,
            dueAmountController: _dueAmountController,
            paidAmountFocusNode: _paidAmountFocusNode,
            dueAmountFocusNode: _dueAmountFocusNode,
            onPaymentMethodChanged: _onPaymentMethodChanged,
            onPaidAmountChanged: _setPaidAmount,
            onDueAmountChanged: _setDueAmount,
            onPaidAmountEditingComplete: _finishPaidAmountEditing,
            onDueAmountEditingComplete: _finishDueAmountEditing,
          ),
          const SizedBox(height: 12),
          if (state.submissionFailure != null)
            Text(
              state.submissionError ?? 'Invalid payment split.',
              key: NewSalePage.paymentFailureKey,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          const SizedBox(height: 12),
          SizedBox(
            width: double.infinity,
            child: FilledButton(
              key: NewSalePage.submitButtonKey,
              onPressed: state.canSubmit && !state.isSubmitting
                  ? () {
                      unawaited(_submitSale());
                    }
                  : null,
              child: state.isSubmitting
                  ? const Text('Recording...')
                  : const Text('Submit sale'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildCustomerSection(NewSaleState state) {
    final customers = state.availableCustomers;
    final selectedId = state.selectedCustomer?.customerId;
    final selectedValue =
        selectedId != null && customers.any((c) => c.customerId == selectedId)
        ? selectedId
        : null;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text(
              'Customer',
              style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 8),
            if (state.isLoadingCustomers)
              const Center(
                child: Padding(
                  padding: EdgeInsets.symmetric(vertical: 8),
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              )
            else
              DropdownButtonFormField<String?>(
                key: NewSalePage.customerDropdownKey,
                value: selectedValue,
                isExpanded: true,
                hint: const Text('Select customer'),
                items: [
                  const DropdownMenuItem<String?>(
                    value: null,
                    child: Text('Walk-in (No customer)'),
                  ),
                  for (final customer in customers)
                    DropdownMenuItem<String?>(
                      value: customer.customerId,
                      child: Text(customer.name),
                    ),
                ],
                onChanged: (customerId) {
                  ref
                      .read(newSaleControllerProvider.notifier)
                      .selectCustomer(customerId);
                },
              ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton(
                key: NewSalePage.createCustomerButtonKey,
                onPressed: () {
                  unawaited(_openCreateCustomerDialog());
                },
                child: const Text('Add customer'),
              ),
            ),
            if (state.customerLoadFailure != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  'Unable to load customers.',
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Future<void> _openCreateCustomerDialog() async {
    final formKey = GlobalKey<FormState>();
    final nameController = TextEditingController();
    final phoneController = TextEditingController();
    final addressController = TextEditingController();
    String? error;
    bool isSubmitting = false;

    try {
      await showDialog<void>(
        context: context,
        builder: (context) {
          return StatefulBuilder(
            builder: (context, setDialogState) {
              Future<void> submit() async {
                if (!(formKey.currentState?.validate() ?? false)) return;
                if (isSubmitting) return;

                setDialogState(() => isSubmitting = true);

                final address = addressController.text.trim();
                final phone = phoneController.text.trim();
                final created = await ref
                    .read(newSaleControllerProvider.notifier)
                    .createAndSelectCustomer(
                      name: nameController.text.trim(),
                      phoneNumber: phone,
                      address: address.isEmpty ? null : address,
                    );
                if (!mounted) return;

                if (created) {
                  Navigator.of(context).pop();
                  return;
                }

                final failure = ref
                    .read(newSaleControllerProvider)
                    .customerCreateFailure;
                setDialogState(() {
                  isSubmitting = false;
                  error = failure?.message;
                });
              }

              return AlertDialog(
                title: const Text('Create customer'),
                content: Form(
                  key: formKey,
                  child: SizedBox(
                    width: 320,
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        TextFormField(
                          controller: nameController,
                          textInputAction: TextInputAction.next,
                          decoration: const InputDecoration(
                            labelText: 'Customer name',
                          ),
                          validator: (value) {
                            final trimmed = (value ?? '').trim();
                            if (trimmed.isEmpty) {
                              return 'Name is required.';
                            }
                            if (trimmed.length > 180) {
                              return 'Name is too long.';
                            }
                            return null;
                          },
                        ),
                        const SizedBox(height: 8),
                        TextFormField(
                          controller: phoneController,
                          textInputAction: TextInputAction.next,
                          keyboardType: TextInputType.phone,
                          decoration: const InputDecoration(
                            labelText: 'Customer phone',
                          ),
                          validator: (value) {
                            final trimmed = (value ?? '').trim();
                            if (trimmed.isEmpty) {
                              return 'Phone is required.';
                            }
                            if (!RegExp(
                              r'^\+?[0-9]{7,15}$',
                            ).hasMatch(trimmed)) {
                              return 'Enter a valid phone number.';
                            }
                            return null;
                          },
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: addressController,
                          decoration: const InputDecoration(
                            labelText: 'Address',
                          ),
                          maxLines: 3,
                        ),
                        if (error != null) ...[
                          const SizedBox(height: 8),
                          Text(
                            error!,
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
                actions: [
                  TextButton(
                    onPressed: isSubmitting
                        ? null
                        : () => Navigator.of(context).pop(),
                    child: const Text('Cancel'),
                  ),
                  FilledButton(
                    key: NewSalePage.createCustomerSubmitButtonKey,
                    onPressed: isSubmitting ? null : submit,
                    child: isSubmitting
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Create'),
                  ),
                ],
              );
            },
          );
        },
      );
    } finally {
      nameController.dispose();
      phoneController.dispose();
      addressController.dispose();
    }
  }

  Future<void> _onAddGoods(Sellable sellable) async {
    await ref.read(newSaleControllerProvider.notifier).addToCart(sellable);
  }

  Future<void> _submitSale() {
    return ref.read(newSaleControllerProvider.notifier).submit();
  }

  void _decreaseQty(String sellableId) {
    final line = _currentLine(sellableId);
    if (line == null) return;
    ref
        .read(newSaleControllerProvider.notifier)
        .updateCartQuantity(sellableId, line.quantity - 1);
  }

  void _increaseQty(String sellableId) {
    final line = _currentLine(sellableId);
    if (line == null) return;
    ref
        .read(newSaleControllerProvider.notifier)
        .updateCartQuantity(sellableId, line.quantity + 1);
  }

  void _removeFromCart(String sellableId) {
    ref.read(newSaleControllerProvider.notifier).removeFromCart(sellableId);
  }

  void _updateSaleDiscountType(int type) {
    ref.read(newSaleControllerProvider.notifier).updateSaleDiscountType(type);
  }

  void _updateSaleDiscountValue(double value) {
    ref.read(newSaleControllerProvider.notifier).updateSaleDiscountValue(value);
  }

  void _updateCartItemDiscountType(String sellableId, int type) {
    ref
        .read(newSaleControllerProvider.notifier)
        .updateCartItemDiscountType(sellableId, type);
  }

  void _updateCartItemDiscountValue(String sellableId, double value) {
    ref
        .read(newSaleControllerProvider.notifier)
        .updateCartItemDiscountValue(sellableId, value);
  }

  void _updateUnitPrice(String sellableId, double value) {
    ref
        .read(newSaleControllerProvider.notifier)
        .updateCartUnitPrice(sellableId, value);
  }

  void _onPaymentMethodChanged(PaymentMethod method) {
    ref.read(newSaleControllerProvider.notifier).setPaymentMethod(method);
  }

  void _setPaidAmount(double paidAmount) {
    ref.read(newSaleControllerProvider.notifier).setPaidAmount(paidAmount);
  }

  void _setDueAmount(double dueAmount) {
    ref.read(newSaleControllerProvider.notifier).setDueAmount(dueAmount);
  }

  NewSaleCartLine? _currentLine(String sellableId) {
    final cart = ref.read(newSaleControllerProvider).cartLines;
    for (final line in cart) {
      if (line.sellable.id == sellableId) return line;
    }
    return null;
  }

  void _syncController(TextEditingController controller, String value) {
    if (controller.text == value) return;
    controller.value = TextEditingValue(
      text: value,
      selection: TextSelection.collapsed(offset: value.length),
    );
  }

  void _syncAmountController(
    TextEditingController controller,
    FocusNode focusNode,
    double value,
  ) {
    if (focusNode.hasFocus) return;
    final next = value.toStringAsFixed(2);
    if (controller.text == next) return;
    controller.value = TextEditingValue(
      text: next,
      selection: TextSelection.collapsed(offset: next.length),
    );
  }

  void _handlePaidAmountFocusChange() {
    _syncAmountController(
      _paidAmountController,
      _paidAmountFocusNode,
      ref.read(newSaleControllerProvider).paidAmount,
    );
  }

  void _handleDueAmountFocusChange() {
    _syncAmountController(
      _dueAmountController,
      _dueAmountFocusNode,
      ref.read(newSaleControllerProvider).dueAmount,
    );
  }

  void _finishPaidAmountEditing() {
    _paidAmountFocusNode.unfocus();
  }

  void _finishDueAmountEditing() {
    _dueAmountFocusNode.unfocus();
  }

  void _searchByBarcode() {
    unawaited(
      ref
          .read(newSaleControllerProvider.notifier)
          .search(barcode: _barcodeController.text),
    );
  }

  void _showRecordedSaleReceipt(BuildContext context, SaleDetail sale) {
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Receipt', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 12),
              _ReceiptRow(label: 'Invoice', value: sale.invoiceNumber),
              _ReceiptRow(label: 'Total', value: formatInr(sale.totalAmount)),
              if (sale.customerName != null)
                _ReceiptRow(label: 'Customer', value: sale.customerName!),
              if (sale.customerPhone != null)
                _ReceiptRow(label: 'Phone', value: sale.customerPhone!),
              const SizedBox(height: 8),
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('Close'),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _showRecordedSaleDetail(BuildContext context, SaleDetail sale) {
    unawaited(showSaleDetailSheet(context, sale: _toSaleListItem(sale)));
  }

  SaleListItem _toSaleListItem(SaleDetail sale) {
    return SaleListItem(
      saleId: sale.saleId,
      invoiceNumber: sale.invoiceNumber,
      customerId: sale.customerId,
      paymentMethod: sale.paymentMethod,
      soldAt: sale.soldAt,
      paidAmount: sale.paidAmount,
      dueAmount: sale.dueAmount,
      totalBeforeDiscount: sale.totalBeforeDiscount,
      totalDiscountAmount: sale.totalDiscountAmount,
      totalAmount: sale.totalAmount,
      totalTaxAmount: sale.totalTaxAmount,
      customerName: sale.customerName,
      customerPhone: sale.customerPhone,
      itemCount: sale.items.length,
      returnNumbers: const [],
      status: sale.status ?? 'paid',
      refundAmount: sale.refundAmount,
      dueReductionAmount: sale.dueReductionAmount,
    );
  }
}

class _ReceiptRow extends StatelessWidget {
  const _ReceiptRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Row(
        children: [
          Expanded(
            child: Text(label, style: Theme.of(context).textTheme.bodySmall),
          ),
          Expanded(
            child: Text(
              value,
              textAlign: TextAlign.end,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
