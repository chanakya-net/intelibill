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
    final theme = Theme.of(context);

    if (sellables.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(
            'No sellables found.',
            style: theme.textTheme.bodyLarge?.copyWith(
              color: const Color(0xFF9A6B45),
            ),
          ),
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
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 12, 8),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              sellable.name,
                              style: theme.textTheme.bodyLarge?.copyWith(
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                          _KindChip(kind: sellable.kind),
                        ],
                      ),
                      const SizedBox(height: 4),
                      Text(
                        sellable.isService
                            ? '${sellable.barcode ?? '-'} • ₹${sellable.price}'
                            : '${sellable.barcode ?? '-'} • '
                                  'Stock ${_formatQuantity(sellable.stock)} • '
                                  '₹${sellable.price}',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: const Color(0xFF9A6B45),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                FilledButton(
                  key: Key('add-button-${sellable.id}'),
                  onPressed: () => onAdd(sellable),
                  child: const Text('Add'),
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

class _KindChip extends StatelessWidget {
  const _KindChip({required this.kind});

  final String kind;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(left: 8),
      child: Chip(
        label: Text(kind),
        visualDensity: VisualDensity.compact,
        backgroundColor: const Color(0xFFFFEDD5),
        side: BorderSide(
          color: theme.colorScheme.primary.withValues(alpha: 0.4),
        ),
        labelStyle: theme.textTheme.labelSmall?.copyWith(
          color: const Color(0xFF7C2D12),
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
