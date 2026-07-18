import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_progress.dart';

class PurchaseOrderDetailSummary extends StatelessWidget {
  const PurchaseOrderDetailSummary({required this.purchaseOrder, super.key});

  final PurchaseOrder purchaseOrder;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Lines: ${purchaseOrder.lines.length}'),
            const SizedBox(height: 8),
            Text('Expected quantity: ${purchaseOrder.expectedQuantity}'),
            Text('Received quantity: ${purchaseOrder.receivedQuantity}'),
            Text('Remaining quantity: ${purchaseOrder.remainingQuantity}'),
            const SizedBox(height: 12),
            PurchaseOrderProgress(
              expectedQuantity: purchaseOrder.expectedQuantity,
              receivedQuantity: purchaseOrder.receivedQuantity,
            ),
            const SizedBox(height: 8),
            Text(
              'Expected total: ${formatInr(purchaseOrder.expectedTotal)}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ],
        ),
      ),
    );
  }
}
