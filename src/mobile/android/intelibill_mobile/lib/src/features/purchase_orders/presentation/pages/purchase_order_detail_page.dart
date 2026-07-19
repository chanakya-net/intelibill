import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_cancel_sheet.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_close_sheet.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_header.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_detail_summary.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_lifecycle_metadata.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_line_card.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_place_sheet.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_receipt_history.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';

class PurchaseOrderDetailPage extends ConsumerWidget {
  const PurchaseOrderDetailPage({required this.purchaseOrderId, super.key});

  static const pageKey = Key('purchase-order-detail-page');
  static const receiveButtonKey = Key('purchase-order-detail-receive-button');
  static const editButtonKey = Key('purchase-order-detail-edit-button');
  static const previewButtonKey = Key('purchase-order-detail-preview-button');

  final String purchaseOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(
      purchaseOrderDetailControllerProvider(purchaseOrderId),
    );
    final suppliersState = ref.watch(suppliersControllerProvider);

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
          if (purchaseOrder.status == PurchaseOrderStatus.draft)
            _actionButton(
              key: editButtonKey,
              icon: Icons.edit_outlined,
              label: l10n.commonEdit,
              onPressed: () => context.go(
                AppRoutes.purchaseOrderEditFor(purchaseOrderId),
              ),
            ),
          _actionButton(
            key: previewButtonKey,
            icon: Icons.picture_as_pdf_outlined,
            label: l10n.purchaseOrderPreviewAction,
            onPressed: () => context.go(
              AppRoutes.purchaseOrderPrintFor(purchaseOrderId),
            ),
          ),
          if (purchaseOrder.status == PurchaseOrderStatus.placed ||
              purchaseOrder.status == PurchaseOrderStatus.partiallyReceived)
            _actionButton(
              key: receiveButtonKey,
              icon: Icons.move_to_inbox,
              label: l10n.purchaseOrderReceiveAction,
              onPressed: () => context.go(
                AppRoutes.purchaseOrderReceiveFor(purchaseOrderId),
              ),
            ),
          if (purchaseOrder.status == PurchaseOrderStatus.placed)
            _actionButton(
              key: const Key('purchase-order-detail-cancel-button'),
              icon: Icons.cancel,
              label: l10n.purchaseOrderCancelTitle,
              onPressed: () {
                unawaited(
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
                  ),
                );
              },
            ),
          if (purchaseOrder.status == PurchaseOrderStatus.partiallyReceived)
            _actionButton(
              key: const Key('purchase-order-detail-close-button'),
              icon: Icons.lock,
              label: l10n.purchaseOrderCloseTitle,
              onPressed: () {
                unawaited(
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
                  ),
                );
              },
            ),
          if (purchaseOrder.status == PurchaseOrderStatus.draft)
            _actionButton(
              key: const Key('purchase-order-detail-delete-button'),
              icon: Icons.delete_outline,
              label: l10n.purchaseOrderDetailDeleteAction,
              onPressed: state.deleteState.isLoading
                  ? null
                  : () => _showDeleteDraftConfirmation(
                      context,
                      l10n,
                      ref,
                      purchaseOrderId,
                    ),
            ),
          if (purchaseOrder.status == PurchaseOrderStatus.draft)
            _actionButton(
              key: const Key('purchase-order-detail-place-button'),
              icon: Icons.send,
              label: l10n.purchaseOrderPlaceAction,
              onPressed: _canPlace(purchaseOrder, suppliersState.suppliers)
                  ? () {
                      unawaited(
                        showPurchaseOrderPlaceSheet(
                          context,
                          onPlace: () {
                            return ref
                                .read(
                                  purchaseOrderDetailControllerProvider(
                                    purchaseOrderId,
                                  ).notifier,
                                )
                                .place();
                          },
                        ),
                      );
                    }
                  : null,
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

  bool _canPlace(PurchaseOrder po, List<Supplier> allSuppliers) {
    if (po.supplierId == null || po.lines.isEmpty) return false;
    Supplier? selectedSupplier;
    for (final supplier in allSuppliers) {
      if (supplier.supplierId == po.supplierId) {
        selectedSupplier = supplier;
        break;
      }
    }
    if (selectedSupplier == null || !selectedSupplier.isActive) return false;
    return po.lines.any(
      (line) => line.expectedQuantity > 0 && line.unitCost >= 0,
    );
  }

  Widget _actionButton({
    required Key key,
    required IconData icon,
    required String label,
    required VoidCallback? onPressed,
  }) {
    return Semantics(
      label: label,
      button: true,
      enabled: onPressed != null,
      child: IconButton(
        key: key,
        icon: Icon(icon),
        tooltip: label,
        onPressed: onPressed,
      ),
    );
  }

  String _failureMessage(AppLocalizations l10n, Failure? failure) {
    return failure == null
        ? l10n.purchaseOrderFailureUnknown
        : purchaseOrderFailureMessage(l10n, failure);
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
            Semantics(liveRegion: true, child: Text(message)),
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

Future<void> _showDeleteDraftConfirmation(
  BuildContext context,
  AppLocalizations l10n,
  WidgetRef ref,
  String purchaseOrderId,
) async {
  final isConfirmed = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      title: Text(l10n.purchaseOrderDetailDeleteConfirmTitle),
      content: Text(l10n.purchaseOrderDetailDeleteConfirmBody),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(dialogContext).pop(false),
          child: Text(l10n.commonCancel),
        ),
        TextButton(
          onPressed: () => Navigator.of(dialogContext).pop(true),
          child: Text(
            l10n.purchaseOrderDetailDeleteAction,
            style: const TextStyle(color: Colors.red),
          ),
        ),
      ],
    ),
  );

  if (!context.mounted || isConfirmed != true) return;

  final controller = ref.read(
    purchaseOrderDetailControllerProvider(purchaseOrderId).notifier,
  );

  try {
    final deleted = await controller.delete();
    if (deleted && context.mounted) {
      context.go(AppRoutes.purchaseOrders);
    }
  } on Object {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l10n.purchaseOrderDetailDeleteErrorGeneric)),
      );
    }
  }
}
