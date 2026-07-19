import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_preview_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/documents/purchase_order_pdf_builder.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_providers.dart';

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
    final exportService = ref.watch(documentExportServiceProvider);
    return DocumentPreviewScaffold(
      key: pageKey,
      descriptor: DocumentDescriptor(
        title: 'Purchase order preview',
        filename: builder.filenameFor(order),
      ),
      onBuild: (_) => builder.build(order, state.shop),
      onPrint: (bytes) => _handlePrint(
        context,
        ref,
        exportService,
        bytes,
        builder.filenameFor(order),
      ),
      onShare: (bytes) => _handleShare(
        context,
        ref,
        exportService,
        bytes,
        builder.filenameFor(order),
      ),
    );
  }

  Future<void> _handlePrint(
    BuildContext context,
    WidgetRef ref,
    dynamic exportService,
    Uint8List bytes,
    String filename,
  ) async {
    final descriptor = DocumentDescriptor(
      title: 'Purchase order preview',
      filename: filename,
    );
    final result = await exportService.print(
      bytes: bytes,
      descriptor: descriptor,
    );

    if (result != null && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Print failed: ${result.message}')),
      );
    }
  }

  Future<void> _handleShare(
    BuildContext context,
    WidgetRef ref,
    dynamic exportService,
    Uint8List bytes,
    String filename,
  ) async {
    final descriptor = DocumentDescriptor(
      title: 'Purchase order preview',
      filename: filename,
    );
    final result = await exportService.share(
      bytes: bytes,
      descriptor: descriptor,
    );

    if (result != null && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Share failed: ${result.message}')),
      );
    }
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
