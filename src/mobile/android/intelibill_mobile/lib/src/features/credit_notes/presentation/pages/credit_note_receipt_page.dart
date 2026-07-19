import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/documents/credit_note_receipt_pdf_builder.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:pdf/pdf.dart';

class CreditNoteReceiptPage extends ConsumerWidget {
  const CreditNoteReceiptPage({required this.code, super.key});

  final String code;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final printState = ref.watch(creditNotePrintByCodeProvider(code));
    final l10n = AppLocalizations.of(context)!;

    return printState.when(
      data: (printData) {
        final builder = CreditNoteReceiptPdfBuilder();
        final descriptor = DocumentDescriptor(
          title: l10n.creditNotesReceiptTitle,
          filename: builder.filenameFor(printData),
          pageFormat: DocumentPageFormat.mm80,
        );

        return DocumentPreviewScaffold(
          descriptor: descriptor,
          onBuild: (format) => builder.build(printData),
          onFailure: (message) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text(message)),
            );
          },
        );
      },
      error: (error, _) => Scaffold(
        appBar: AppBar(
          title: Text(l10n.creditNotesReceiptTitle),
        ),
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
                TextButton(
                  onPressed: () =>
                      ref.invalidate(creditNotePrintByCodeProvider(code)),
                  child: Text(l10n.creditNotesRetry),
                ),
              ],
            ),
          ),
        ),
      ),
      loading: () => Scaffold(
        appBar: AppBar(
          title: Text(l10n.creditNotesReceiptTitle),
        ),
        body: const Center(child: CircularProgressIndicator()),
      ),
    );
  }
}
