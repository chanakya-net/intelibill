import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_widgets.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_status_badge.dart';
import 'package:intl/intl.dart';

class PurchaseOrderDetailHeader extends StatelessWidget {
  const PurchaseOrderDetailHeader({required this.purchaseOrder, super.key});

  final PurchaseOrder purchaseOrder;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final locale = Localizations.localeOf(context).toLanguageTag();
    final dateOnlyFormat = DateFormat.yMMMd(locale);
    final createdAt = DateFormat.yMMMd(locale).add_jm().format(
      purchaseOrder.createdAt,
    );
    final supplierName = purchaseOrder.supplierName?.isNotEmpty == true
        ? purchaseOrder.supplierName!
        : l10n.purchaseOrderDetailNotProvided;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(
                    purchaseOrder.purchaseOrderNumber,
                    style: theme.textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
                PurchaseOrderStatusBadge(status: purchaseOrder.status),
              ],
            ),
            const SizedBox(height: 12),
            const Divider(height: 24),
            PurchaseOrderDetailInfoLine(
              label: l10n.purchaseOrderDetailSupplier,
              value: supplierName,
            ),
            if (purchaseOrder.supplierReferenceNumber?.isNotEmpty == true)
              PurchaseOrderDetailInfoLine(
                label: l10n.purchaseOrderDetailSupplierReferenceNumber,
                value: purchaseOrder.supplierReferenceNumber!,
              ),
            if (purchaseOrder.supplierReference?.isNotEmpty == true)
              PurchaseOrderDetailInfoLine(
                label: l10n.purchaseOrderDetailSupplierReference,
                value: purchaseOrder.supplierReference!,
              ),
            PurchaseOrderDetailInfoLine(
              label: l10n.purchaseOrderDetailCreatedAt,
              value: createdAt,
            ),
            if (purchaseOrder.orderDate != null)
              PurchaseOrderDetailInfoLine(
                label: l10n.purchaseOrderDetailOrderDate,
                value: dateOnlyFormat.format(purchaseOrder.orderDate!),
              ),
            if (purchaseOrder.expectedDeliveryDate != null)
              PurchaseOrderDetailInfoLine(
                label: l10n.purchaseOrderDetailExpectedDeliveryDate,
                value: dateOnlyFormat.format(
                  purchaseOrder.expectedDeliveryDate!,
                ),
              ),
            if (purchaseOrder.notes?.isNotEmpty == true) ...[
              const Divider(height: 24),
              PurchaseOrderDetailInfoLine(
                label: l10n.purchaseOrderDetailNotes,
                value: purchaseOrder.notes!,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
