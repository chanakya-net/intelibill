import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:pdf/pdf.dart';
import 'package:printing/printing.dart';

typedef DocumentBytesBuilder = Future<Uint8List> Function(PdfPageFormat format);

class DocumentPreviewScaffold extends StatelessWidget {
  const DocumentPreviewScaffold({
    required this.descriptor,
    required this.onBuild,
    super.key,
  });

  final DocumentDescriptor descriptor;
  final DocumentBytesBuilder onBuild;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(descriptor.title)),
      body: PdfPreview(
        build: (_) => onBuild(descriptor.pdfPageFormat),
        initialPageFormat: descriptor.pdfPageFormat,
        pdfFileName: descriptor.filename,
        canChangePageFormat: false,
      ),
    );
  }
}
