import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_cancel_sheet.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_close_sheet.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_header.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_summary.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_lifecycle_metadata.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_line_card.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_receipt_history.dart';

class PurchaseOrderDetailPage extends ConsumerWidget {
  const PurchaseOrderDetailPage({required this.purchaseOrderId, super.key});

  static const pageKey = Key('purchase-order-detail-page');
  static const receiveButtonKey = Key('purchase-order-detail-receive-button');

  final String purchaseOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(
      purchaseOrderDetailControllerProvider(purchaseOrderId),
    );

    if (state.isLoading && state.detail == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.commonLoading)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (state.failure != null && state.detail == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.purchaseOrderDetailPageTitle)),
        body: _FailureView(
          isNotFound: state.failure is NotFoundFailure,
          message: _failureMessage(l10n, state.failure),
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
      return const Scaffold(key: pageKey, body: SizedBox.shrink());
    }

    return Scaffold(
      key: pageKey,
      appBar: AppBar(
        title: Text(purchaseOrder.purchaseOrderNumber),
        actions: [
          if (purchaseOrder.status == PurchaseOrderStatus.placed ||
              purchaseOrder.status == PurchaseOrderStatus.partiallyReceived)
            IconButton(
              key: receiveButtonKey,
              icon: const Icon(Icons.move_to_inbox),
              tooltip: l10n.purchaseOrderReceiveAction,
              onPressed: () {
                context.go(
                  AppRoutes.purchaseOrderReceiveFor(purchaseOrderId),
                );
              },
            ),
          if (purchaseOrder.status == PurchaseOrderStatus.placed)
            IconButton(
              key: const Key('purchase-order-detail-cancel-button'),
              icon: const Icon(Icons.cancel),
              onPressed: () {
                showPurchaseOrderCancelSheet(
                  context,
                  onCancel: (reason) {
                    return ref
                        .read(
                          purchaseOrderDetailControllerProvider(
                            purchaseOrderId,
                          ).notifier,
                        )
                        .cancel(reason);
                  },
                );
              },
            ),
          if (purchaseOrder.status == PurchaseOrderStatus.partiallyReceived)
            IconButton(
              key: const Key('purchase-order-detail-close-button'),
              icon: const Icon(Icons.lock),
              onPressed: () {
                showPurchaseOrderCloseSheet(
                  context,
                  onClose: (reason) {
                    return ref
                        .read(
                          purchaseOrderDetailControllerProvider(
                            purchaseOrderId,
                          ).notifier,
                        )
                        .close(reason);
                  },
                );
              },
            ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => ref
            .read(
              purchaseOrderDetailControllerProvider(purchaseOrderId).notifier,
            )
            .refresh(),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            PurchaseOrderDetailHeader(purchaseOrder: purchaseOrder),
            PurchaseOrderDetailSummary(purchaseOrder: purchaseOrder),
            PurchaseOrderLifecycleMetadata(purchaseOrder: purchaseOrder),
            _LinesSection(lines: purchaseOrder.lines),
            PurchaseOrderReceiptHistory(receipts: purchaseOrder.receipts),
          ],
        ),
      ),
    );
  }

  String _failureMessage(AppLocalizations l10n, Failure? failure) {
    if (failure is NotFoundFailure) {
      return l10n.purchaseOrderDetailNotFound;
    }
    return l10n.purchaseOrderDetailUnableToLoad;
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
    final l10n = AppLocalizations.of(context)!;
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
                child: Text(l10n.purchaseOrderDetailBack),
              ),
            ] else ...[
              FilledButton(
                onPressed: onRetry,
                child: Text(l10n.purchaseOrderDetailRetry),
              ),
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
    final l10n = AppLocalizations.of(context)!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
          child: Text(l10n.purchaseOrderDetailLinesHeader),
        ),
        if (lines.isEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
            child: Text(l10n.purchaseOrderDetailNoLines),
          )
        else
          ...lines.map((line) => PurchaseOrderLineCard(line: line)),
      ],
    );
  }
}
