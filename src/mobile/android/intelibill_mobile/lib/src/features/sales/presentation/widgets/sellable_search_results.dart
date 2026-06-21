import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

typedef OnAddSellable = void Function(Sellable sellable);

class SellableSearchResults extends StatelessWidget {
  const SellableSearchResults({
    super.key,
    required this.sellables,
    required this.onAdd,
  });

  final List<Sellable> sellables;
  final OnAddSellable onAdd;

  @override
  Widget build(BuildContext context) {
    if (sellables.isEmpty) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Text('No sellables found.'),
        ),
      );
    }

    return ListView.separated(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
      itemCount: sellables.length,
      separatorBuilder: (_, _) => const SizedBox(height: 8),
      itemBuilder: (context, index) {
        final sellable = sellables[index];
        return Card(
          child: ListTile(
            title: Row(
              children: [
                Expanded(child: Text(sellable.name)),
                _KindChip(kind: sellable.kind),
              ],
            ),
            subtitle: Text(
              sellable.isService
                  ? '${sellable.barcode ?? '-'} • ₹${sellable.price}'
                  : '${sellable.barcode ?? '-'} • '
                        'Stock ${_formatQuantity(sellable.stock)} • ₹${sellable.price}',
            ),
            trailing: FilledButton(
              key: Key('add-button-${sellable.id}'),
              onPressed: () => onAdd(sellable),
              child: const Text('Add'),
            ),
          ),
        );
      },
    );
  }

  String _formatQuantity(double quantity) {
    final whole = quantity.truncateToDouble();
    if (quantity == whole) {
      return whole.toInt().toString();
    }
    return quantity.toString();
  }
}

class _KindChip extends StatelessWidget {
  const _KindChip({required this.kind});

  final String kind;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(left: 8),
      child: Chip(
        label: Text(kind),
        visualDensity: VisualDensity.compact,
      ),
    );
  }
}
