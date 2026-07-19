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
  final Function({required int expectedQuantity, required double unitCost})
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
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.line.description,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            Text(
              '${l10n.purchaseOrderBuilderItemIdLabel}: ${widget.line.itemId}',
              style: Theme.of(context).textTheme.bodySmall,
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
                const SizedBox(width: 8),
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
            const SizedBox(height: 8),
            Text(
              '${l10n.purchaseOrderBuilderLineTotalLabel}: '
              '${(widget.line.lineTotal).toStringAsFixed(2)}',
              style: Theme.of(context).textTheme.labelLarge,
            ),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerRight,
              child: FilledButton.tonal(
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
