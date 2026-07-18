import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_preview_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/documents/purchase_order_pdf_builder.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';

class PurchaseOrderPreviewPage extends ConsumerWidget {
  const PurchaseOrderPreviewPage({required this.purchaseOrderId, super.key});

  static const pageKey = Key('purchase-order-preview-page');
  final String purchaseOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(
      purchaseOrderPreviewControllerProvider(purchaseOrderId),
    );
    final order = state.purchaseOrder;
    if (state.isLoading && order == null) {
      return _loading();
    }
    if (state.failure != null && order == null) {
      return _failure(ref);
    }
    if (order == null) {
      return const Scaffold(key: pageKey, body: SizedBox.shrink());
    }

    final builder = PurchaseOrderPdfBuilder();
    return DocumentPreviewScaffold(
      key: pageKey,
      descriptor: DocumentDescriptor(
        title: 'Purchase order preview',
        filename: builder.filenameFor(order),
      ),
      onBuild: (_) => builder.build(order, state.shop),
    );
  }

  Widget _loading() => const Scaffold(
    key: pageKey,
    body: Center(child: CircularProgressIndicator()),
  );

  Widget _failure(WidgetRef ref) => Scaffold(
    key: pageKey,
    appBar: AppBar(title: const Text('Purchase order preview')),
    body: Center(
      child: FilledButton(
        onPressed: () => ref
            .read(
              purchaseOrderPreviewControllerProvider(purchaseOrderId).notifier,
            )
            .retry(),
        child: const Text('Retry'),
      ),
    ),
  );
}
