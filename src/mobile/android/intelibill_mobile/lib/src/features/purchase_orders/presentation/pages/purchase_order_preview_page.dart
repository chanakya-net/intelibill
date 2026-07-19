import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
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
    final locale = Localizations.localeOf(context);
    final order = state.purchaseOrder;
    if (state.isLoading && order == null) {
      return _loading();
    }
    if (state.failure != null && order == null) {
      return _failure(context, ref, state.failure!);
    }
    if (order == null) {
      return const Scaffold(key: pageKey, body: SizedBox.shrink());
    }

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

  Widget _loading() => const Scaffold(
    key: pageKey,
    body: Center(child: CircularProgressIndicator()),
  );

  Widget _failure(BuildContext context, WidgetRef ref, Failure failure) =>
      Scaffold(
        key: pageKey,
        appBar: AppBar(
          title: Text(AppLocalizations.of(context)!.purchaseOrderPreviewTitle),
        ),
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                purchaseOrderFailureMessage(
                  AppLocalizations.of(context)!,
                  failure,
                ),
              ),
              FilledButton(
                onPressed: () => ref
                    .read(
                      purchaseOrderPreviewControllerProvider(
                        purchaseOrderId,
                      ).notifier,
                    )
                    .retry(),
                child: Text(
                  AppLocalizations.of(context)!.purchaseOrdersRetry,
                ),
              ),
            ],
          ),
        ),
      );
}
