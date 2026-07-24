import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/documents/credit_note_receipt_pdf_builder.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_providers.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_service.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_messages.dart';

class CreditNoteReceiptPage extends ConsumerWidget {
  const CreditNoteReceiptPage({required this.code, super.key});

  static const pageKey = Key('credit-note-receipt-page');

  final String code;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final printState = ref.watch(creditNotePrintByCodeProvider(code));
    final l10n = AppLocalizations.of(context)!;

    return printState.when(
      loading: () => Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.creditNotesReceiptTitle)),
        body: const Center(child: CircularProgressIndicator()),
      ),
      error: (error, _) => Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.creditNotesReceiptTitle)),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline, size: 48),
                const SizedBox(height: 12),
                Text(l10n.customersErrorGeneric),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () =>
                      ref.invalidate(creditNotePrintByCodeProvider(code)),
                  child: Text(l10n.creditNotesRetry),
                ),
              ],
            ),
          ),
        ),
      ),
      data: (printData) {
        final builder = CreditNoteReceiptPdfBuilder();
        final descriptor = DocumentDescriptor(
          title: l10n.creditNotesReceiptTitle,
          filename: builder.filenameFor(printData),
          pageFormat: DocumentPageFormat.mm80,
        );
        final exportService = ref.watch(documentExportServiceProvider);

        return DocumentPreviewScaffold(
          key: pageKey,
          descriptor: descriptor,
          onBuild: (format) => builder.build(
            printData,
            locale: Localizations.localeOf(context),
          ),
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
