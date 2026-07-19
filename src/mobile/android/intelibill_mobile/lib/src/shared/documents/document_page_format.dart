import 'package:pdf/pdf.dart';

enum DocumentPageFormat { a4, mm80 }

extension DocumentPageFormatX on DocumentPageFormat {
  PdfPageFormat get pdfPageFormat {
    switch (this) {
      case DocumentPageFormat.a4:
        return PdfPageFormat.a4;
      case DocumentPageFormat.mm80:
        return PdfPageFormat.roll80;
    }
  }
}
