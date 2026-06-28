import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/widgets/credit_note_receipt_view.dart';

class CreditNoteReceiptPage extends ConsumerWidget {
  const CreditNoteReceiptPage({required this.code, super.key});

  final String code;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final printState = ref.watch(creditNotePrintByCodeProvider(code));
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.creditNotesReceiptTitle),
      ),
      body: printState.when(
        data: (printData) => _buildContent(context, printData),
        error: (error, _) => _buildFailure(context, ref),
        loading: () => const Center(child: CircularProgressIndicator()),
      ),
    );
  }

  Widget _buildContent(BuildContext context, CreditNotePrint receiptData) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: CreditNoteReceiptView(
              note: receiptData,
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildFailure(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;

    return Center(
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
    );
  }
}
