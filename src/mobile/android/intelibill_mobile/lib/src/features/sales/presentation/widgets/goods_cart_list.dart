import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

typedef OnDecrease = void Function(String sellableId);
typedef OnIncrease = void Function(String sellableId);
typedef OnRemove = void Function(String sellableId);

class GoodsCartList extends StatelessWidget {
  const GoodsCartList({
    super.key,
    required this.lines,
    required this.onDecrease,
    required this.onIncrease,
    required this.onRemove,
    required this.total,
  });

  final List<NewSaleCartLine> lines;
  final OnDecrease onDecrease;
  final OnIncrease onIncrease;
  final OnRemove onRemove;
  final double total;

  @override
  Widget build(BuildContext context) {
    if (lines.isEmpty) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Text('Cart is empty.'),
        ),
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(0, 8, 0, 8),
      itemCount: lines.length + 1,
      separatorBuilder: (_, __) => const SizedBox(height: 8),
      itemBuilder: (context, index) {
        if (index == lines.length) {
          return Padding(
            padding: const EdgeInsets.symmetric(horizontal: 8),
            child: Text(
              'Total: ₹${total.toStringAsFixed(2)}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
          );
        }

        final line = lines[index];
        return Card(
          child: Padding(
            padding: const EdgeInsets.all(8),
            child: Column(
              children: [
                Row(
                  children: [
                    Expanded(child: Text(line.sellable.name)),
                    IconButton(
                      key: Key('remove-from-cart-${line.sellable.id}'),
                      icon: const Icon(Icons.delete_outline),
                      onPressed: () => onRemove(line.sellable.id),
                    ),
                  ],
                ),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text('Qty: ${_formatQuantity(line.quantity)}'),
                    Text('₹${line.lineTotal.toStringAsFixed(2)}'),
                  ],
                ),
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    IconButton(
                      key: Key('decrease-${line.sellable.id}'),
                      icon: const Icon(Icons.remove_circle_outline),
                      onPressed: () => onDecrease(line.sellable.id),
                    ),
                    IconButton(
                      key: Key('increase-${line.sellable.id}'),
                      icon: const Icon(Icons.add_circle_outline),
                      onPressed: () => onIncrease(line.sellable.id),
                    ),
                  ],
                ),
              ],
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
