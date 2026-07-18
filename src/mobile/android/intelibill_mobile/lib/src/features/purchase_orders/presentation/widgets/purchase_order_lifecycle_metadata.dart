import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intl/intl.dart';

class PurchaseOrderLifecycleMetadata extends StatelessWidget {
  const PurchaseOrderLifecycleMetadata({
    required this.purchaseOrder,
    super.key,
  });

  final PurchaseOrder purchaseOrder;

  @override
  Widget build(BuildContext context) {
    if (!_hasMetadata) return const SizedBox.shrink();
    final l10n = AppLocalizations.of(context)!;
    final locale = Localizations.localeOf(context).toLanguageTag();
    final dateFormat = DateFormat.yMMMd(locale).add_jm();
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (purchaseOrder.cancellationReason?.isNotEmpty == true)
              Text(
                '${l10n.purchaseOrderDetailCancellationReason}: '
                '${purchaseOrder.cancellationReason}',
              ),
            if (purchaseOrder.closedAt != null)
              Text(
                '${l10n.purchaseOrderDetailClosedAt}: '
                '${dateFormat.format(purchaseOrder.closedAt!)}',
              ),
            if (purchaseOrder.closedBy?.isNotEmpty == true)
              Text(
                '${l10n.purchaseOrderDetailClosedBy}: ${purchaseOrder.closedBy}',
              ),
            if (purchaseOrder.closeReason?.isNotEmpty == true)
              Text(
                '${l10n.purchaseOrderDetailCloseReason}: '
                '${purchaseOrder.closeReason}',
              ),
          ],
        ),
      ),
    );
  }

  bool get _hasMetadata =>
      purchaseOrder.cancellationReason?.isNotEmpty == true ||
      purchaseOrder.closedAt != null ||
      purchaseOrder.closedBy?.isNotEmpty == true ||
      purchaseOrder.closeReason?.isNotEmpty == true;
}
