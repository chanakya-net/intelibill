import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';

class PurchaseOrderStatusBadge extends StatelessWidget {
  const PurchaseOrderStatusBadge({required this.status, super.key});

  final PurchaseOrderStatus status;

  @override
  Widget build(BuildContext context) {
    final color = _color(context);
    final label = purchaseOrderStatusMessage(
      AppLocalizations.of(context)!,
      status,
    );
    return Semantics(
      key: Key('purchase-order-status-${status.name}'),
      container: true,
      label: AppLocalizations.of(context)!.purchaseOrderDocumentStatus(label),
      child: ExcludeSemantics(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
          decoration: BoxDecoration(
            color: color.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(16),
          ),
          child: Text(
            label,
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: color,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
      ),
    );
  }

  Color _color(BuildContext context) {
    return switch (status) {
      PurchaseOrderStatus.draft => const Color(0xFF64748B),
      PurchaseOrderStatus.placed => const Color(0xFF0369A1),
      PurchaseOrderStatus.partiallyReceived => const Color(0xFFB45309),
      PurchaseOrderStatus.received => const Color(0xFF15803D),
      PurchaseOrderStatus.closed => const Color(0xFF475569),
      PurchaseOrderStatus.cancelled => Theme.of(context).colorScheme.error,
    };
  }
}
