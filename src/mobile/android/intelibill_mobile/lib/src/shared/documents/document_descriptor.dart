import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:pdf/pdf.dart';

class DocumentDescriptor {
  const DocumentDescriptor({
    required this.title,
    required this.filename,
    this.pageFormat = DocumentPageFormat.a4,
  });

  final String title;
  final String filename;
  final DocumentPageFormat pageFormat;

  PdfPageFormat get pdfPageFormat => pageFormat.pdfPageFormat;
}
