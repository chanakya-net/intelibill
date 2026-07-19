import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/filename_sanitizer.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf/document_font_resolver.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_formatters.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_theme.dart';
import 'package:intl/intl.dart';
import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;

class CreditNoteReceiptPdfBuilder {
  CreditNoteReceiptPdfBuilder({DocumentFontResolver? fontResolver})
    : _fontResolver = fontResolver ?? DocumentFontResolver();

  final DocumentFontResolver _fontResolver;

  String filenameFor(CreditNotePrint note) => FilenameSanitizer.sanitize(
    'credit-note-${note.code}.pdf',
  );

  String contentFor(CreditNotePrint note) => [
    'Credit Note',
    'Code: ${note.code}',
    'Status: ${_statusLabel(note.status)}',
    'Invoice: ${note.invoiceNumber}',
    'Return: ${note.returnNumber}',
    'Customer: ${note.customerDisplayName}',
    'Reason: ${note.reason}',
    if (note.voidReason != null && note.voidReason!.isNotEmpty)
      'Void Reason: ${note.voidReason}',
    'Issued: ${_date(note.issuedAt)}',
    'Expires: ${note.expiresAt != null ? _date(note.expiresAt!) : 'No expiry'}',
    'Original: ${_money(note.originalAmount)}',
    'Available: ${_money(note.availableBalance)}',
  ].join('\n');

  Future<Uint8List> build(
    CreditNotePrint note, {
    Locale locale = pdfDefaultLocale,
  }) async {
    final theme = PdfDocumentTheme(await _fontResolver.resolve(locale));
    final document = pw.Document(
      compress: false,
      subject: locale.toLanguageTag(),
    );
    document.addPage(
      pw.MultiPage(
        theme: theme.data,
        pageFormat: _receiptPageFormat(note),
        build: (_) => [
          pw.Text('Credit Note', style: theme.title),
          pw.SizedBox(height: 12),
          pw.Text('Code: ${note.code}'),
          pw.Text('Status: ${_statusLabel(note.status)}'),
          pw.Text('Invoice: ${note.invoiceNumber}'),
          pw.Text('Return: ${note.returnNumber}'),
          pw.Text('Customer: ${note.customerDisplayName}'),
          pw.Text('Reason: ${note.reason}'),
          if (note.voidReason != null && note.voidReason!.isNotEmpty) ...[
            pw.SizedBox(height: 8),
            pw.Text('Void Reason: ${note.voidReason}'),
          ],
          pw.SizedBox(height: 12),
          pw.Text('Issued: ${_date(note.issuedAt)}'),
          pw.Text(
            'Expires: ${note.expiresAt != null ? _date(note.expiresAt!) : 'No expiry'}',
          ),
          pw.SizedBox(height: 12),
          _amountLine('Original', _money(note.originalAmount)),
          _amountLine('Available', _money(note.availableBalance)),
        ],
      ),
    );
    return document.save();
  }

  PdfPageFormat _receiptPageFormat(CreditNotePrint note) {
    const baseRows = 9;
    final voidReasonRows =
        (note.voidReason != null && note.voidReason!.isNotEmpty) ? 2 : 0;
    final rowEstimate = baseRows + voidReasonRows;
    final estimatedHeight = (rowEstimate * 16.0) + 80;
    final compactHeight = estimatedHeight.clamp(120.0, 780.0);

    return DocumentPageFormat.mm80.pdfPageFormat.copyWith(
      height: compactHeight,
    );
  }

  pw.Widget _amountLine(String label, String value) => pw.Row(
    mainAxisAlignment: pw.MainAxisAlignment.spaceBetween,
    children: [
      pw.Text(label),
      pw.Text(value),
    ],
  );

  String _money(num value) => 'Rs ${value.toStringAsFixed(0)}';

  String _statusLabel(String status) {
    return switch (status.toLowerCase()) {
      'active' => 'Active',
      'expired' => 'Expired',
      'voided' => 'Voided',
      _ => status,
    };
  }

  String _date(DateTime dateTime) {
    return DateFormat('dd MMM yyyy, h:mm a').format(dateTime.toLocal());
  }
}
