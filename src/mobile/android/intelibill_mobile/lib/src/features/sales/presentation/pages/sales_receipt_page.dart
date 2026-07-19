import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/documents/sale_receipt_pdf_builder.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_providers.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_service.dart';

class SalesReceiptPage extends ConsumerWidget {
  const SalesReceiptPage({required this.saleId, this.initialSale, super.key});

  static const pageKey = Key('sales-receipt-page');

  final String saleId;
  final SaleDetail? initialSale;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(saleDetailControllerProvider(saleId));
    final l10n = AppLocalizations.of(context)!;
    final sale = state.detail ?? initialSale;

    if (state.isLoading && sale == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (state.failure != null && sale == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline, size: 48),
                const SizedBox(height: 12),
                Text(l10n.salesDetailUnableToLoad),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () => ref
                      .read(saleDetailControllerProvider(saleId).notifier)
                      .refresh(),
                  child: Text(l10n.salesHistoryRetry),
                ),
              ],
            ),
          ),
        ),
      );
    }

    if (sale == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
        body: const SizedBox.shrink(),
      );
    }

    final builder = SaleReceiptPdfBuilder();
    final exportService = ref.watch(documentExportServiceProvider);
    final descriptor = DocumentDescriptor(
      title: l10n.salesDetailReceipt,
      filename: builder.filenameFor(sale),
      pageFormat: DocumentPageFormat.mm80,
    );
    return DocumentPreviewScaffold(
      key: pageKey,
      descriptor: descriptor,
      onBuild: (_) => builder.build(sale),
      onPrint: (bytes) =>
          _handlePrint(context, exportService, bytes, descriptor),
      onShare: (bytes) =>
          _handleShare(context, exportService, bytes, descriptor),
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
        SnackBar(content: Text('Print failed: ${result.message}')),
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
        SnackBar(content: Text('Share failed: ${result.message}')),
      );
    }
  }
}
