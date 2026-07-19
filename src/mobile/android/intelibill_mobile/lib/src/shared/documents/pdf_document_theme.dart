import 'package:intelibill_mobile/src/shared/documents/pdf/document_font_resolver.dart';
import 'package:pdf/widgets.dart' as pw;

class PdfDocumentTheme {
  const PdfDocumentTheme(this.fonts);

  final PdfFontSet fonts;

  pw.ThemeData get data => pw.ThemeData.withFont(
    base: fonts.regular,
    bold: fonts.bold,
    fontFallback: fonts.fallback,
  );

  pw.TextStyle get title => _style(fontSize: 20, bold: true);
  pw.TextStyle get emphasis => _style(bold: true);

  pw.Widget sectionLabel(String value) => pw.Text(value, style: emphasis);

  pw.TextStyle _style({double? fontSize, bool bold = false}) => pw.TextStyle(
    font: fonts.regular,
    fontNormal: fonts.regular,
    fontBold: fonts.bold,
    fontFallback: fonts.fallback,
    fontSize: fontSize,
    fontWeight: bold ? pw.FontWeight.bold : pw.FontWeight.normal,
  );
}
