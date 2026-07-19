import 'dart:async';
import 'dart:typed_data';

import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_gateway.dart';
import 'package:pdf/pdf.dart';

enum ExportOperation { print, share, build }

typedef PdfBuilder = Future<Uint8List> Function(PdfPageFormat format);

class ExportFailure {
  ExportFailure({
    required this.operation,
    required this.message,
  });

  final ExportOperation operation;
  final String message;
}

class DocumentExportService {
  DocumentExportService(this._gateway);

  final DocumentOutputGateway _gateway;
  Completer<void>? _printCompleter;
  Completer<void>? _shareCompleter;
  ExportFailure? _printResult;
  ExportFailure? _shareResult;

  Future<ExportFailure?> print({
    required Uint8List bytes,
    required DocumentDescriptor descriptor,
  }) async {
    if (_printCompleter != null && !_printCompleter!.isCompleted) {
      await _printCompleter!.future;
      return _printResult;
    }

    _printCompleter = Completer<void>();
    _printResult = null;
    try {
      await _gateway.print(bytes: bytes, filename: descriptor.filename);
      _printCompleter!.complete();
      return null;
    } on PlatformPrintFailure catch (e) {
      _printResult = ExportFailure(
        operation: ExportOperation.print,
        message: e.message,
      );
      _printCompleter!.complete();
      return _printResult;
    } on Object catch (e) {
      _printResult = ExportFailure(
        operation: ExportOperation.print,
        message: e.toString(),
      );
      _printCompleter!.complete();
      return _printResult;
    }
  }

  Future<ExportFailure?> share({
    required Uint8List bytes,
    required DocumentDescriptor descriptor,
  }) async {
    if (_shareCompleter != null && !_shareCompleter!.isCompleted) {
      await _shareCompleter!.future;
      return _shareResult;
    }

    _shareCompleter = Completer<void>();
    _shareResult = null;
    try {
      await _gateway.share(
        bytes: bytes,
        filename: descriptor.filename,
        title: descriptor.title,
      );
      _shareCompleter!.complete();
      return null;
    } on PlatformShareFailure catch (e) {
      _shareResult = ExportFailure(
        operation: ExportOperation.share,
        message: e.message,
      );
      _shareCompleter!.complete();
      return _shareResult;
    } on Object catch (e) {
      _shareResult = ExportFailure(
        operation: ExportOperation.share,
        message: e.toString(),
      );
      _shareCompleter!.complete();
      return _shareResult;
    }
  }

  Future<ExportFailure?> buildPdf({
    required PdfBuilder builder,
    required DocumentDescriptor descriptor,
  }) async {
    try {
      await builder(descriptor.pdfPageFormat);
      return null;
    } on Object catch (e) {
      return ExportFailure(
        operation: ExportOperation.build,
        message: e.toString(),
      );
    }
  }
}
