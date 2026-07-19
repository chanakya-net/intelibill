import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
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
              '${l10n.purchaseOrderDetailSupplier}: '
              '${purchaseOrder.supplierName?.isNotEmpty == true ? purchaseOrder.supplierName : l10n.purchaseOrderDetailNotProvided}',
            ),
            if (purchaseOrder.supplierReferenceNumber?.isNotEmpty == true)
              Text(
                '${l10n.purchaseOrderDetailSupplierReferenceNumber} '
                '${purchaseOrder.supplierReferenceNumber}',
              ),
            if (purchaseOrder.supplierReference?.isNotEmpty == true)
              Text(
                '${l10n.purchaseOrderDetailSupplierReference} '
                '${purchaseOrder.supplierReference}',
              ),
            Text('${l10n.purchaseOrderDetailCreatedAt}: $createdAt'),
            if (purchaseOrder.orderDate != null)
              Text(
                '${l10n.purchaseOrderDetailOrderDate}: '
                '${dateOnlyFormat.format(purchaseOrder.orderDate!)}',
              ),
            if (purchaseOrder.expectedDeliveryDate != null)
              Text(
                '${l10n.purchaseOrderDetailExpectedDeliveryDate}: '
                '${dateOnlyFormat.format(purchaseOrder.expectedDeliveryDate!)}',
              ),
            if (purchaseOrder.notes?.isNotEmpty == true) ...[
              const SizedBox(height: 8),
              Text('${l10n.purchaseOrderDetailNotes}: ${purchaseOrder.notes}'),
            ],
          ],
        ),
      ),
    );
  }
}
