import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/documents/credit_note_receipt_pdf_builder.dart';
import 'package:intl/intl.dart';
import 'package:pdf/pdf.dart';

void main() {
  final populated = _creditNotePrint();
  final noExpiry = _creditNotePrintNoExpiry();

  test('filename follows credit-note-{code}.pdf', () {
    final builder = CreditNoteReceiptPdfBuilder();

    expect(builder.filenameFor(populated), 'credit-note-CN-REC-001.pdf');
  });

  test('contentFor maps populated credit note values', () {
    final builder = CreditNoteReceiptPdfBuilder();
    final content = builder.contentFor(populated);

    expect(content, contains('Credit Note'));
    expect(content, contains('Code: CN-REC-001'));
    expect(content, contains('Status: Active'));
    expect(content, contains('Invoice: INV-001'));
    expect(content, contains('Return: RET-001'));
    expect(content, contains('Customer: Alice'));
    expect(content, contains('Reason: Defective item'));
    expect(
      content,
      contains(
        'Issued: ${DateFormat('dd MMM yyyy, h:mm a').format(populated.issuedAt)}',
      ),
    );
    expect(
      content,
      contains(
        'Expires: ${DateFormat('dd MMM yyyy, h:mm a').format(populated.expiresAt!)}',
      ),
    );
    expect(content, contains('Original: Rs 1000'));
    expect(content, contains('Available: Rs 800'));
  });

  test('contentFor preserves no-expiry value when null', () {
    final builder = CreditNoteReceiptPdfBuilder();
    final content = builder.contentFor(noExpiry);

    expect(
      content,
      contains(
        'Issued: ${DateFormat('dd MMM yyyy, h:mm a').format(noExpiry.issuedAt)}',
      ),
    );
    expect(content, contains('Expires: No expiry'));
  });

  test('contentFor includes void reason when present', () {
    final voidedNote = _creditNotePrintVoided();
    final builder = CreditNoteReceiptPdfBuilder();
    final content = builder.contentFor(voidedNote);

    expect(content, contains('Status: Voided'));
    expect(content, contains('Void Reason: Payment applied'));
  });

  test(
    'builds 80 mm PDF bytes with compact page geometry',
    () async {
      final builder = CreditNoteReceiptPdfBuilder();
      final bytes = await builder.build(populated);
      final payload = latin1.decode(bytes, allowInvalid: true);
      final mediaBox = _extractMediaBox(payload);

      expect(bytes, isNotEmpty);
      expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
      expect(payload, isNot(contains('₹')));
      expect(mediaBox.width, closeTo(PdfPageFormat.roll80.width, 0.5));
      expect(mediaBox.height, greaterThan(0));
      expect(mediaBox.height, lessThan(PdfPageFormat.a4.height));
      expect(payload, contains('CN-REC-001'));
      expect(payload, contains('INV-001'));
      expect(
        _containsTokenSequence(payload, const [
          'Credit',
          'Note',
          'Code',
          'CN-REC-001',
        ]),
        isTrue,
      );
    },
  );

  test('handles expired status', () {
    final builder = CreditNoteReceiptPdfBuilder();
    final expired = CreditNotePrint(
      creditNoteId: 'cn-exp',
      code: 'CN-EXP-001',
      status: 'expired',
      isUsable: false,
      originalAmount: 500,
      availableBalance: 0,
      issuedAt: DateTime(2026, 5, 1),
      expiresAt: DateTime(2026, 6, 1),
      saleId: 'sale-exp',
      invoiceNumber: 'INV-EXP-001',
      saleReturnId: 'ret-exp',
      returnNumber: 'RET-EXP-001',
      customerDisplayName: 'Bob',
      reason: 'Wrong size',
      voidReason: null,
    );

    final content = builder.contentFor(expired);
    expect(content, contains('Status: Expired'));
  });

  test('handles voided status with reason', () {
    final builder = CreditNoteReceiptPdfBuilder();
    final voided = _creditNotePrintVoided();

    final content = builder.contentFor(voided);
    expect(content, contains('Status: Voided'));
    expect(content, contains('Void Reason: Payment applied'));
  });
}

bool _containsTokenSequence(String text, List<String> tokens) {
  var index = -1;
  for (final token in tokens) {
    index = text.indexOf(token, index + 1);
    if (index == -1) {
      return false;
    }
  }
  return true;
}

_CreditNoteMediaBox _extractMediaBox(String payload) {
  final match = RegExp(r'/MediaBox\s*\[(.*?)\]').firstMatch(payload);
  expect(match, isNotNull, reason: 'PDF payload should include a MediaBox');

  final boxValues = match!.group(1)!.trim().split(RegExp(r'\s+'));
  expect(boxValues, hasLength(4));

  final values = boxValues.map<double>(double.parse).toList();
  return _CreditNoteMediaBox(
    left: values[0],
    bottom: values[1],
    right: values[2],
    top: values[3],
  );
}

class _CreditNoteMediaBox {
  _CreditNoteMediaBox({
    required this.left,
    required this.bottom,
    required this.right,
    required this.top,
  });

  final double left;
  final double bottom;
  final double right;
  final double top;

  double get width => right - left;
  double get height => top - bottom;
}

CreditNotePrint _creditNotePrint() {
  return CreditNotePrint(
    creditNoteId: 'cn-1',
    code: 'CN-REC-001',
    status: 'active',
    isUsable: true,
    originalAmount: 1000,
    availableBalance: 800,
    issuedAt: DateTime(2026, 6, 10, 10),
    expiresAt: DateTime(2026, 7, 10, 10),
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    saleReturnId: 'ret-1',
    returnNumber: 'RET-001',
    customerDisplayName: 'Alice',
    reason: 'Defective item',
    voidReason: null,
  );
}

CreditNotePrint _creditNotePrintNoExpiry() {
  return CreditNotePrint(
    creditNoteId: 'cn-2',
    code: 'CN-NOEXP-001',
    status: 'active',
    isUsable: true,
    originalAmount: 500,
    availableBalance: 500,
    issuedAt: DateTime(2026, 6, 1),
    expiresAt: null,
    saleId: 'sale-2',
    invoiceNumber: 'INV-002',
    saleReturnId: 'ret-2',
    returnNumber: 'RET-002',
    customerDisplayName: 'Charlie',
    reason: 'Store policy',
    voidReason: null,
  );
}

CreditNotePrint _creditNotePrintVoided() {
  return CreditNotePrint(
    creditNoteId: 'cn-3',
    code: 'CN-VOID-001',
    status: 'voided',
    isUsable: false,
    originalAmount: 1500,
    availableBalance: 1500,
    issuedAt: DateTime(2026, 6, 5),
    expiresAt: DateTime(2026, 7, 5),
    saleId: 'sale-3',
    invoiceNumber: 'INV-003',
    saleReturnId: 'ret-3',
    returnNumber: 'RET-003',
    customerDisplayName: 'David',
    reason: 'Customer request',
    voidReason: 'Payment applied',
  );
}
