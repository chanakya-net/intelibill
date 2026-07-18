import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';

class PurchaseOrderLineCard extends StatelessWidget {
  const PurchaseOrderLineCard({required this.line, super.key});

  final PurchaseOrderLine line;

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              line.description,
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: 6),
            Text('Expected: ${line.expectedQuantity}'),
            Text('Received: ${line.receivedQuantity}'),
            Text('Remaining: ${line.remainingQuantity}'),
            Text('Unit cost: ${formatInr(line.unitCost)}'),
            Text('Line total: ${formatInr(line.lineTotal)}'),
          ],
        ),
      ),
    );
  }
}
