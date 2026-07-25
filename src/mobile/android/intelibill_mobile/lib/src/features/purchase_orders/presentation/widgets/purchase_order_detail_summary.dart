import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_widgets.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_progress.dart';

class PurchaseOrderDetailSummary extends StatelessWidget {
  const PurchaseOrderDetailSummary({required this.purchaseOrder, super.key});

  final PurchaseOrder purchaseOrder;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);

    return PurchaseOrderDetailSectionCard(
      title:
          '${l10n.purchaseOrderDetailLinesHeader}: '
          '${purchaseOrder.lines.length}',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          PurchaseOrderDetailInfoLine(
            label: l10n.purchaseOrderDetailExpectedQuantity,
            value: '${purchaseOrder.expectedQuantity}',
          ),
          PurchaseOrderDetailInfoLine(
            label: l10n.purchaseOrderDetailReceivedQuantity,
            value: '${purchaseOrder.receivedQuantity}',
          ),
          PurchaseOrderDetailInfoLine(
            label: l10n.purchaseOrderDetailRemainingQuantity,
            value: '${purchaseOrder.remainingQuantity}',
          ),
          const SizedBox(height: 12),
          PurchaseOrderProgress(
            expectedQuantity: purchaseOrder.expectedQuantity,
            receivedQuantity: purchaseOrder.receivedQuantity,
          ),
          const Divider(height: 24),
          Text(
            '${l10n.purchaseOrderDetailExpectedTotal}: '
            '${formatInr(purchaseOrder.expectedTotal)}',
            style: theme.textTheme.headlineSmall?.copyWith(
              fontWeight: FontWeight.w800,
            ),
          ),
        ],
      ),
    );
  }
}
