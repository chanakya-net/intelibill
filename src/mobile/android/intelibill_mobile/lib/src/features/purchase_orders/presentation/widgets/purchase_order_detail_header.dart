import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_status_badge.dart';
import 'package:intl/intl.dart';

class PurchaseOrderDetailHeader extends StatelessWidget {
  const PurchaseOrderDetailHeader({required this.purchaseOrder, super.key});

  final PurchaseOrder purchaseOrder;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              purchaseOrder.purchaseOrderNumber,
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 8),
            PurchaseOrderStatusBadge(status: purchaseOrder.status),
            const SizedBox(height: 8),
            Text(
              purchaseOrder.supplierName?.isNotEmpty == true
                  ? 'Supplier: ${purchaseOrder.supplierName}'
                  : 'Supplier: Not provided',
            ),
            if (purchaseOrder.supplierReferenceNumber?.isNotEmpty == true)
              Text(
                'Supplier reference number: ${purchaseOrder.supplierReferenceNumber}',
              ),
            if (purchaseOrder.supplierReference?.isNotEmpty == true)
              Text('Supplier reference: ${purchaseOrder.supplierReference}'),
            if (purchaseOrder.orderDate != null)
              Text(
                'Order date: ${DateFormat('d MMM yyyy').format(purchaseOrder.orderDate!)}',
              ),
            if (purchaseOrder.expectedDeliveryDate != null)
              Text(
                'Expected delivery: ${DateFormat('d MMM yyyy').format(purchaseOrder.expectedDeliveryDate!)}',
              ),
            if (purchaseOrder.notes?.isNotEmpty == true) ...[
              const SizedBox(height: 8),
              Text('Notes: ${purchaseOrder.notes}'),
            ],
          ],
        ),
      ),
    );
  }
}
