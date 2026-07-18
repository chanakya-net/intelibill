import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';

class PurchaseOrderLineCard extends StatelessWidget {
  const PurchaseOrderLineCard({required this.line, super.key});

  final PurchaseOrderLine line;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
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
            Text(
              '${l10n.purchaseOrderLineExpectedQuantity}: ${line.expectedQuantity}',
            ),
            Text(
              '${l10n.purchaseOrderLineReceivedQuantity}: ${line.receivedQuantity}',
            ),
            Text(
              '${l10n.purchaseOrderLineRemainingQuantity}: ${line.remainingQuantity}',
            ),
            Text(
              '${l10n.purchaseOrderLineUnitCost}: ${formatInr(line.unitCost)}',
            ),
            Text(
              '${l10n.purchaseOrderLineTotal}: ${formatInr(line.lineTotal)}',
            ),
          ],
        ),
      ),
    );
  }
}
