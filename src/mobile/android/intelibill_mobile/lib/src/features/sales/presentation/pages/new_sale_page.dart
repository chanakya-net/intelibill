import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/goods_cart_list.dart';
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
                total: state.cartTotal,
                onDecrease: _decreaseQty,
                onIncrease: (sellableId) => _increaseQty(sellableId),
                onRemove: _removeFromCart,
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
                onPressed: () => ref
                    .read(newSaleControllerProvider.notifier)
                    .search(barcode: _barcodeController.text),
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
        ],
      ),
    );
  }

  void _onAddGoods(Sellable sellable) {
    ref.read(newSaleControllerProvider.notifier).addToCart(sellable);
  }

  void _decreaseQty(String sellableId) {
    final line = _currentLine(sellableId);
    if (line == null) return;
    final notifier = ref.read(newSaleControllerProvider.notifier);
    notifier.updateCartQuantity(sellableId, line.quantity - 1);
  }

  void _increaseQty(String sellableId) {
    final line = _currentLine(sellableId);
    if (line == null) return;
    final notifier = ref.read(newSaleControllerProvider.notifier);
    notifier.updateCartQuantity(sellableId, line.quantity + 1);
  }

  void _removeFromCart(String sellableId) {
    ref.read(newSaleControllerProvider.notifier).removeFromCart(sellableId);
  }

  NewSaleCartLine? _currentLine(String sellableId) {
    final cart = ref.read(newSaleControllerProvider).cartLines;
    for (final line in cart) {
      if (line.sellable.id == sellableId) return line;
    }
    return null;
  }
}
