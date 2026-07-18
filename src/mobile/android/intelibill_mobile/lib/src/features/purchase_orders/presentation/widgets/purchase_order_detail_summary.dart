import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_progress.dart';

class PurchaseOrderDetailSummary extends StatelessWidget {
  const PurchaseOrderDetailSummary({required this.purchaseOrder, super.key});

  final PurchaseOrder purchaseOrder;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${l10n.purchaseOrderDetailLinesHeader}: ${purchaseOrder.lines.length}',
            ),
            const SizedBox(height: 8),
            Text(
              '${l10n.purchaseOrderDetailExpectedQuantity}: '
              '${purchaseOrder.expectedQuantity}',
            ),
            Text(
              '${l10n.purchaseOrderDetailReceivedQuantity}: '
              '${purchaseOrder.receivedQuantity}',
            ),
            Text(
              '${l10n.purchaseOrderDetailRemainingQuantity}: '
              '${purchaseOrder.remainingQuantity}',
            ),
            const SizedBox(height: 12),
            PurchaseOrderProgress(
              expectedQuantity: purchaseOrder.expectedQuantity,
              receivedQuantity: purchaseOrder.receivedQuantity,
            ),
            const SizedBox(height: 8),
            Text(
              '${l10n.purchaseOrderDetailExpectedTotal}: '
              '${formatInr(purchaseOrder.expectedTotal)}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ],
        ),
      ),
    );
  }
}
