import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/filename_sanitizer.dart';
import 'package:pdf/pdf.dart';

void main() {
  test('descriptor is immutable and retains A4 format', () {
    const descriptor = DocumentDescriptor(
      title: 'Purchase order',
      filename: 'purchase-order-PO-1.pdf',
    );

    expect(descriptor.title, 'Purchase order');
    expect(descriptor.pageFormat, DocumentPageFormat.a4);
    expect(descriptor.pdfPageFormat, PdfPageFormat.a4);
  });

  test(
    'filename sanitizer preserves safe names and replaces unsafe characters',
    () {
      expect(
        FilenameSanitizer.sanitize('purchase-order-PO / 12:3.pdf'),
        'purchase-order-PO-12-3.pdf',
      );
      expect(FilenameSanitizer.sanitize('  .pdf  '), 'document.pdf');
    },
  );

  test('descriptor supports 80 mm format', () {
    const descriptor = DocumentDescriptor(
      title: 'Receipt',
      filename: 'receipt.pdf',
      pageFormat: DocumentPageFormat.mm80,
    );

    expect(descriptor.pageFormat, DocumentPageFormat.mm80);
    expect(descriptor.pdfPageFormat, PdfPageFormat.roll80);
  });

  test('fake byte builders receive the descriptor A4 format', () async {
    Future<Uint8List> fakeBuilder(PdfPageFormat format) async {
      expect(format, PdfPageFormat.a4);
      return Uint8List.fromList('%PDF'.codeUnits);
    }

    const descriptor = DocumentDescriptor(title: 'Test', filename: 'test.pdf');
    expect(await fakeBuilder(descriptor.pdfPageFormat), isNotEmpty);
  });
}
