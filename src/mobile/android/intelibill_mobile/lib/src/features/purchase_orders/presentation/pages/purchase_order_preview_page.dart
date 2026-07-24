import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_preview_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/documents/purchase_order_pdf_builder.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_providers.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_service.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_messages.dart';

class PurchaseOrderPreviewPage extends ConsumerWidget {
  const PurchaseOrderPreviewPage({required this.purchaseOrderId, super.key});

  static const pageKey = Key('purchase-order-preview-page');
  final String purchaseOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(
      purchaseOrderPreviewControllerProvider(purchaseOrderId),
    );
    final order = state.purchaseOrder;

    if (state.isLoading && order == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.purchaseOrderPreviewTitle)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (state.failure != null && order == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.purchaseOrderPreviewTitle)),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline, size: 48),
                const SizedBox(height: 12),
                Text(purchaseOrderFailureMessage(l10n, state.failure!)),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () => ref
                      .read(
                        purchaseOrderPreviewControllerProvider(
                          purchaseOrderId,
                        ).notifier,
                      )
                      .retry(),
                  child: Text(l10n.purchaseOrdersRetry),
                ),
              ],
            ),
          ),
        ),
      );
    }

    if (order == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.purchaseOrderPreviewTitle)),
        body: const SizedBox.shrink(),
      );
    }

    final locale = Localizations.localeOf(context);
    final builder = PurchaseOrderPdfBuilder();
    final exportService = ref.watch(documentExportServiceProvider);
    final descriptor = DocumentDescriptor(
      title: l10n.purchaseOrderPreviewTitle,
      filename: builder.filenameFor(order),
    );
    return DocumentPreviewScaffold(
      key: pageKey,
      descriptor: descriptor,
      onBuild: (_) => builder.build(
        order,
        state.shop,
        l10n,
        locale: locale,
      ),
      onPrint: (bytes) => _handlePrint(
        context,
        exportService,
        bytes,
        descriptor,
      ),
      onShare: (bytes) => _handleShare(
        context,
        exportService,
        bytes,
        descriptor,
      ),
      onFailure: (message) {
        if (context.mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text(message)),
          );
        }
      },
    );
  }

  Future<void> _handlePrint(
    BuildContext context,
    DocumentExportService exportService,
    Uint8List bytes,
    DocumentDescriptor descriptor,
  ) async {
    final result = await exportService.print(
      bytes: bytes,
      descriptor: descriptor,
    );

    if (result != null && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            documentOutputFailureMessage(
              AppLocalizations.of(context)!,
              result.operation,
            ),
          ),
        ),
      );
    }
  }

  Future<void> _handleShare(
    BuildContext context,
    DocumentExportService exportService,
    Uint8List bytes,
    DocumentDescriptor descriptor,
  ) async {
    final result = await exportService.share(
      bytes: bytes,
      descriptor: descriptor,
    );

    if (result != null && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            documentOutputFailureMessage(
              AppLocalizations.of(context)!,
              result.operation,
            ),
          ),
        ),
      );
    }
  }
}
