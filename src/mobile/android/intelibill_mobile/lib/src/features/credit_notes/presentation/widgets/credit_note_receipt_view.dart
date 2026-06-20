import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intl/intl.dart';

class CreditNoteReceiptView extends StatelessWidget {
  const CreditNoteReceiptView({
    required this.note,
    this.formatter = formatInr,
    super.key,
  });

  final CreditNotePrint note;
  final String Function(num?) formatter;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(note.code, style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 12),
        _ReceiptRow(
          label: l10n.creditNotesStatusLabel,
          value: _statusLabel(l10n),
        ),
        _ReceiptRow(
          label: l10n.creditNotesReceiptCodeLabel,
          value: note.code,
        ),
        _ReceiptRow(
          label: l10n.creditNotesInvoiceLabel,
          value: note.invoiceNumber,
        ),
        _ReceiptRow(
          label: l10n.creditNotesReturnLabel,
          value: note.returnNumber,
        ),
        _ReceiptRow(
          label: l10n.creditNotesReceiptCustomerLabel,
          value: note.customerDisplayName,
        ),
        _ReceiptRow(
          label: l10n.creditNotesReceiptReasonLabel,
          value: note.reason,
        ),
        if (note.voidReason != null && note.voidReason!.isNotEmpty)
          _ReceiptRow(
            label: l10n.creditNotesReceiptVoidReasonLabel,
            value: note.voidReason!,
          ),
        const Divider(height: 24),
        _ReceiptRow(
          label: l10n.creditNotesOriginalAmountLabel,
          value: formatter(note.originalAmount),
        ),
        _ReceiptRow(
          label: l10n.creditNotesBalanceLabel,
          value: formatter(note.availableBalance),
        ),
        _ReceiptRow(
          label: l10n.creditNotesReceiptIssueDateLabel,
          value: dateFormat.format(note.issuedAt.toLocal()),
        ),
        _ReceiptRow(
          label: l10n.creditNotesReceiptExpiryDateLabel,
          value: note.expiresAt == null
              ? l10n.creditNotesNoExpiry
              : dateFormat.format(note.expiresAt!.toLocal()),
        ),
      ],
    );
  }

  String _statusLabel(AppLocalizations l10n) {
    return switch (note.status.toLowerCase()) {
      'active' => l10n.creditNotesReceiptStatusActive,
      'expired' => l10n.creditNotesReceiptStatusExpired,
      'voided' => l10n.creditNotesReceiptStatusVoided,
      _ => note.status,
    };
  }
}

class _ReceiptRow extends StatelessWidget {
  const _ReceiptRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Row(
        children: [
          Expanded(
            child: Text(
              label,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
