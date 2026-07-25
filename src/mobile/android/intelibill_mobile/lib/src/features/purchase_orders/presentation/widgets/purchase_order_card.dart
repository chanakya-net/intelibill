import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
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
    final l10n = AppLocalizations.of(context)!;
    final dateFormat = DateFormat('d MMM yyyy');
    final useStackedHeader = MediaQuery.textScalerOf(context).scale(1) > 1.3;
    final orderIdentity = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          purchaseOrder.purchaseOrderNumber,
          style: theme.textTheme.titleSmall?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 4),
        Text(
          purchaseOrder.supplierName ?? l10n.purchaseOrderCardNoSupplier,
          style: theme.textTheme.bodyMedium,
        ),
        if (purchaseOrder.supplierReference?.isNotEmpty == true) ...[
          const SizedBox(height: 2),
          Text(
            l10n.purchaseOrderCardReference(
              purchaseOrder.supplierReference!,
            ),
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
        ],
      ],
    );

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        key: Key('purchase-order-card-${purchaseOrder.purchaseOrderId}'),
        borderRadius: BorderRadius.circular(22),
        onTap:
            onTap ??
            () {
              GoRouter.of(context).go(
                AppRoutes.purchaseOrderDetailFor(purchaseOrder.purchaseOrderId),
              );
            },
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (useStackedHeader)
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    orderIdentity,
                    const SizedBox(height: 8),
                    PurchaseOrderStatusBadge(status: purchaseOrder.status),
                  ],
                )
              else
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: orderIdentity),
                    PurchaseOrderStatusBadge(status: purchaseOrder.status),
                  ],
                ),
              const SizedBox(height: 10),
              Text(
                dateFormat.format(purchaseOrder.createdAt),
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 6,
                children: [
                  _MetaChip(
                    label: l10n.purchaseOrderCardLineCount(
                      purchaseOrder.lineCount,
                    ),
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                  _MetaChip(
                    label: l10n.purchaseOrderCardExpectedQuantity(
                      purchaseOrder.expectedQuantity,
                    ),
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ],
              ),
              const SizedBox(height: 10),
              PurchaseOrderProgress(
                expectedQuantity: purchaseOrder.expectedQuantity,
                receivedQuantity: purchaseOrder.receivedQuantity,
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      formatInr(purchaseOrder.expectedTotal),
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                  Icon(
                    Icons.chevron_right,
                    color: theme.colorScheme.onSurface.withValues(alpha: 0.4),
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

class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: color.withValues(alpha: 0.25)),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: color,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
