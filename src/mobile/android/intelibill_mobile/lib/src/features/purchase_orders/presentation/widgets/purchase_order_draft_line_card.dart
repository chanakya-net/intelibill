import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';

class PurchaseOrderDraftLineCard extends StatefulWidget {
  const PurchaseOrderDraftLineCard({
    required this.line,
    required this.onUpdate,
    required this.onRemove,
    super.key,
  });

  final PurchaseOrderDraftLine line;
  final void Function({required int expectedQuantity, required double unitCost})
  onUpdate;
  final VoidCallback onRemove;

  @override
  State<PurchaseOrderDraftLineCard> createState() =>
      _PurchaseOrderDraftLineCardState();
}

class _PurchaseOrderDraftLineCardState
    extends State<PurchaseOrderDraftLineCard> {
  late TextEditingController _quantityController;
  late TextEditingController _unitCostController;

  @override
  void initState() {
    super.initState();
    _quantityController = TextEditingController(
      text: widget.line.expectedQuantity.toString(),
    );
    _unitCostController = TextEditingController(
      text: widget.line.unitCost.toStringAsFixed(2),
    );
  }

  @override
  void didUpdateWidget(PurchaseOrderDraftLineCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.line.expectedQuantity.toString() != _quantityController.text) {
      _quantityController.text = widget.line.expectedQuantity.toString();
    }
    final nextCost = widget.line.unitCost.toStringAsFixed(2);
    if (nextCost != _unitCostController.text) {
      _unitCostController.text = nextCost;
    }
  }

  @override
  void dispose() {
    _quantityController.dispose();
    _unitCostController.dispose();
    super.dispose();
  }

  void _updateLine() {
    final qty = int.tryParse(_quantityController.text) ?? 0;
    final cost = double.tryParse(_unitCostController.text) ?? 0.0;
    widget.onUpdate(expectedQuantity: qty, unitCost: cost);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.line.description,
              style: theme.textTheme.titleSmall?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              '${l10n.purchaseOrderBuilderItemIdLabel}: ${widget.line.itemId}',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    controller: _quantityController,
                    keyboardType: TextInputType.number,
                    decoration: InputDecoration(
                      labelText: l10n.purchaseOrderBuilderQuantityLabel,
                    ),
                    onFieldSubmitted: (_) => _updateLine(),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    controller: _unitCostController,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: InputDecoration(
                      labelText: l10n.purchaseOrderBuilderUnitCostLabel,
                    ),
                    onFieldSubmitted: (_) => _updateLine(),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Text(
              '${l10n.purchaseOrderBuilderLineTotalLabel}: '
              '${widget.line.lineTotal.toStringAsFixed(2)}',
              style: theme.textTheme.labelLarge?.copyWith(
                fontWeight: FontWeight.w600,
                color: theme.textTheme.headlineSmall?.color,
              ),
            ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                onPressed: widget.onRemove,
                child: Text(l10n.purchaseOrderBuilderRemoveLineLabel),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
