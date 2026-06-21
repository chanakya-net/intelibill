import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/goods_cart_list.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_detail_sheet.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sellable_search_results.dart';

class NewSalePage extends ConsumerStatefulWidget {
  const NewSalePage({super.key});

  @override
  ConsumerState<NewSalePage> createState() => _NewSalePageState();
}

class _NewSalePageState extends ConsumerState<NewSalePage> {
  late final TextEditingController _searchController;
  late final TextEditingController _barcodeController;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
    _barcodeController = TextEditingController();
  }

  @override
  void dispose() {
    _searchController.dispose();
    _barcodeController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(newSaleControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    _syncController(_searchController, state.searchTerm);
    _syncController(_barcodeController, state.barcodeTerm);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.shellNewSale)),
      body: SafeArea(
        child: Column(
          children: [
            _buildSearchSection(state),
            const Divider(height: 1),
            Expanded(
              child: state.isSearching
                  ? const Center(child: CircularProgressIndicator())
                  : SellableSearchResults(
                      sellables: state.results,
                      onAdd: _onAddGoods,
                    ),
            ),
            const Divider(height: 1),
            Expanded(
              child: GoodsCartList(
                lines: state.cartLines,
                subtotal: state.subtotalAmount,
                tax: state.taxAmount,
                discount: state.discountAmount,
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
                onSubmit: () {
                  return ref.read(newSaleControllerProvider.notifier).submit();
                },
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
                onDecrease: _decreaseQty,
                onIncrease: _increaseQty,
                onRemove: _removeFromCart,
                onUnitPriceChanged: _updateUnitPrice,
              ),
            ),
          ],
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
                        .updateBarcodeTerm(
                          value,
                        );
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

  Future<void> _onAddGoods(Sellable sellable) async {
    await ref.read(newSaleControllerProvider.notifier).addToCart(sellable);
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

  void _updateUnitPrice(String sellableId, double value) {
    ref
        .read(newSaleControllerProvider.notifier)
        .updateCartUnitPrice(sellableId, value);
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
              _ReceiptRow(
                label: 'Invoice',
                value: sale.invoiceNumber,
              ),
              _ReceiptRow(
                label: 'Total',
                value: formatInr(sale.totalAmount),
              ),
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
    unawaited(
      showSaleDetailSheet(context, sale: _toSaleListItem(sale)),
    );
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
  const _ReceiptRow({
    required this.label,
    required this.value,
  });

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
