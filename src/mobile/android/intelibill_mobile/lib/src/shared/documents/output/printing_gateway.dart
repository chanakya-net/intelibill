import 'dart:typed_data';

import 'package:intelibill_mobile/src/shared/documents/output/document_output_gateway.dart';
import 'package:printing/printing.dart' as printing;

class PrintingGateway implements DocumentOutputGateway {
  @override
  Future<void> print({
    required Uint8List bytes,
    required String filename,
  }) async {
    try {
      await printing.Printing.layoutPdf(
        onLayout: (_) async => bytes,
        name: filename,
      );
    } catch (e) {
      throw PlatformPrintFailure(message: e.toString());
    }
  }

  @override
  Future<void> share({
    required Uint8List bytes,
    required String filename,
    required String title,
  }) async {
    try {
      await printing.Printing.sharePdf(
        bytes: bytes,
        filename: filename,
      );
    } catch (e) {
      throw PlatformShareFailure(message: e.toString());
    }
  }
}
