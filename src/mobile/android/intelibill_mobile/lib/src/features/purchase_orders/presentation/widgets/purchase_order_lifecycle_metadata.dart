import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_widgets.dart';
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

    return PurchaseOrderDetailSectionCard(
      title: _sectionTitle(l10n),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (purchaseOrder.cancellationReason?.isNotEmpty == true)
            PurchaseOrderDetailInfoLine(
              label: l10n.purchaseOrderDetailCancellationReason,
              value: purchaseOrder.cancellationReason!,
            ),
          if (purchaseOrder.closedAt != null)
            PurchaseOrderDetailInfoLine(
              label: l10n.purchaseOrderDetailClosedAt,
              value: dateFormat.format(purchaseOrder.closedAt!),
            ),
          if (purchaseOrder.closedBy?.isNotEmpty == true)
            PurchaseOrderDetailInfoLine(
              label: l10n.purchaseOrderDetailClosedBy,
              value: purchaseOrder.closedBy!,
            ),
          if (purchaseOrder.closeReason?.isNotEmpty == true)
            PurchaseOrderDetailInfoLine(
              label: l10n.purchaseOrderDetailCloseReason,
              value: purchaseOrder.closeReason!,
            ),
        ],
      ),
    );
  }

  String _sectionTitle(AppLocalizations l10n) {
    if (purchaseOrder.cancellationReason?.isNotEmpty == true) {
      return l10n.purchaseOrderCancelTitle;
    }
    return l10n.purchaseOrderCloseTitle;
  }

  bool get _hasMetadata =>
      purchaseOrder.cancellationReason?.isNotEmpty == true ||
      purchaseOrder.closedAt != null ||
      purchaseOrder.closedBy?.isNotEmpty == true ||
      purchaseOrder.closeReason?.isNotEmpty == true;
}
