import 'package:pdf/widgets.dart' as pw;

class PdfDocumentTheme {
  const PdfDocumentTheme._();

  static final title = pw.TextStyle(
    fontSize: 20,
    fontWeight: pw.FontWeight.bold,
  );
  static final emphasis = pw.TextStyle(fontWeight: pw.FontWeight.bold);

  static pw.Widget sectionLabel(String value) =>
      pw.Text(value, style: emphasis);
}
