import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_header.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_summary.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_line_card.dart';

class PurchaseOrderDetailPage extends ConsumerWidget {
  const PurchaseOrderDetailPage({required this.purchaseOrderId, super.key});

  static const pageKey = Key('purchase-order-detail-page');

  final String purchaseOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(
      purchaseOrderDetailControllerProvider(purchaseOrderId),
    );

    if (state.isLoading && state.detail == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: const Text('Purchase Order')),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (state.failure != null && state.detail == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: const Text('Purchase order')),
        body: _FailureView(
          isNotFound: state.failure is NotFoundFailure,
          message: _failureMessage(state.failure),
          router: GoRouter.of(context),
          onRetry: () => ref
              .read(
                purchaseOrderDetailControllerProvider(purchaseOrderId).notifier,
              )
              .retry(),
        ),
      );
    }

    final purchaseOrder = state.detail;
    if (purchaseOrder == null) {
      return Scaffold(key: pageKey, body: const SizedBox.shrink());
    }

    return Scaffold(
      key: pageKey,
      appBar: AppBar(title: Text(purchaseOrder.purchaseOrderNumber)),
      body: RefreshIndicator(
        onRefresh: () => ref
            .read(
              purchaseOrderDetailControllerProvider(purchaseOrderId).notifier,
            )
            .refresh(),
        child: ListView(
          children: [
            PurchaseOrderDetailHeader(purchaseOrder: purchaseOrder),
            PurchaseOrderDetailSummary(purchaseOrder: purchaseOrder),
            _LinesSection(lines: purchaseOrder.lines),
          ],
        ),
      ),
    );
  }

  String _failureMessage(Failure? failure) {
    if (failure is NotFoundFailure) {
      return 'Purchase order not found.';
    }
    return 'Could not load purchase order.';
  }
}

class _FailureView extends StatelessWidget {
  const _FailureView({
    required this.isNotFound,
    required this.message,
    required this.router,
    required this.onRetry,
  });

  final bool isNotFound;
  final String message;
  final GoRouter router;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(message),
            const SizedBox(height: 12),
            if (isNotFound) ...[
              FilledButton(
                onPressed: () => router.go(AppRoutes.purchaseOrders),
                child: const Text('Back to purchase orders'),
              ),
            ] else ...[
              FilledButton(onPressed: onRetry, child: const Text('Retry')),
            ],
          ],
        ),
      ),
    );
  }
}

class _LinesSection extends StatelessWidget {
  const _LinesSection({required this.lines});

  final List<PurchaseOrderLine> lines;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.fromLTRB(16, 12, 16, 4),
          child: Text('Lines'),
        ),
        if (lines.isEmpty)
          const Padding(
            padding: EdgeInsets.fromLTRB(16, 8, 16, 24),
            child: Text('No lines on this order'),
          )
        else
          ...lines.map((line) => PurchaseOrderLineCard(line: line)),
      ],
    );
  }
}
