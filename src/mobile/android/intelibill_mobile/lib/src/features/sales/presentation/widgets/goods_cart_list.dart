import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

typedef OnDecrease = void Function(String sellableId);
typedef OnIncrease = void Function(String sellableId);
typedef OnRemove = void Function(String sellableId);
typedef OnUnitPriceChanged = void Function(String sellableId, double value);

class GoodsCartList extends StatelessWidget {
  const GoodsCartList({
    super.key,
    required this.lines,
    required this.onDecrease,
    required this.onIncrease,
    required this.onRemove,
    required this.onUnitPriceChanged,
    required this.total,
  });

  final List<NewSaleCartLine> lines;
  final OnDecrease onDecrease;
  final OnIncrease onIncrease;
  final OnRemove onRemove;
  final OnUnitPriceChanged onUnitPriceChanged;
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
      separatorBuilder: (_, _) => const SizedBox(height: 8),
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
                    Expanded(
                      child: Text(
                        line.sellable.name,
                        key: Key('cart-line-name-${line.sellable.id}'),
                      ),
                    ),
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
                if (line.sellable.isService) ...[
                  const SizedBox(height: 8),
                  TextField(
                    key: Key('service-unit-price-${line.sellable.id}'),
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Unit price',
                      border: OutlineInputBorder(),
                    ),
                    controller: TextEditingController(
                      text: line.effectiveUnitPrice.toStringAsFixed(2),
                    ),
                    onChanged: (value) {
                      final parsed = double.tryParse(value.trim());
                      if (parsed != null) {
                        onUnitPriceChanged(line.sellable.id, parsed);
                      }
                    },
                  ),
                ],
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
