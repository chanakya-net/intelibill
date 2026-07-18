import 'package:pdf/pdf.dart';

enum DocumentPageFormat { a4 }

extension DocumentPageFormatX on DocumentPageFormat {
  PdfPageFormat get pdfPageFormat {
    switch (this) {
      case DocumentPageFormat.a4:
        return PdfPageFormat.a4;
    }
  }
}
