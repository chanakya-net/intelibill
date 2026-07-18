import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_progress.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_status_badge.dart';
import 'package:intl/intl.dart';

class PurchaseOrderCard extends StatelessWidget {
  const PurchaseOrderCard({required this.purchaseOrder, this.onTap, super.key});

  final PurchaseOrderListItem purchaseOrder;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        key: Key('purchase-order-card-${purchaseOrder.purchaseOrderId}'),
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      purchaseOrder.purchaseOrderNumber,
                      style: theme.textTheme.titleSmall?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                  PurchaseOrderStatusBadge(status: purchaseOrder.status),
                ],
              ),
              const SizedBox(height: 6),
              Text(purchaseOrder.supplierName ?? 'No supplier'),
              if (purchaseOrder.supplierReference?.isNotEmpty == true)
                Text('Ref: ${purchaseOrder.supplierReference}'),
              const SizedBox(height: 8),
              Wrap(
                spacing: 12,
                runSpacing: 4,
                children: [
                  Text('${purchaseOrder.lineCount} lines'),
                  Text('Expected: ${purchaseOrder.expectedQuantity}'),
                ],
              ),
              const SizedBox(height: 8),
              PurchaseOrderProgress(
                expectedQuantity: purchaseOrder.expectedQuantity,
                receivedQuantity: purchaseOrder.receivedQuantity,
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Text(
                    formatInr(purchaseOrder.expectedTotal),
                    style: theme.textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const Spacer(),
                  Text(
                    DateFormat('d MMM yyyy').format(purchaseOrder.createdAt),
                  ),
                  const SizedBox(width: 4),
                  Icon(
                    Icons.chevron_right,
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
