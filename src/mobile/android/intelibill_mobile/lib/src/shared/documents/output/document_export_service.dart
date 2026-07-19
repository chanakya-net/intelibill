import 'dart:async';
import 'dart:typed_data';

import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_gateway.dart';

enum ExportOperation { print, share }

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

  Future<ExportFailure?> print({
    required Uint8List bytes,
    required DocumentDescriptor descriptor,
  }) async {
    if (_printCompleter != null && !_printCompleter!.isCompleted) {
      try {
        await _printCompleter!.future;
      } catch (_) {
        return null;
      }
      return null;
    }

    _printCompleter = Completer<void>();
    try {
      await _gateway.print(bytes: bytes, filename: descriptor.filename);
      _printCompleter!.complete();
      return null;
    } on PlatformPrintFailure catch (e) {
      _printCompleter!.complete();
      return ExportFailure(
        operation: ExportOperation.print,
        message: e.message,
      );
    } catch (e) {
      _printCompleter!.complete();
      return ExportFailure(
        operation: ExportOperation.print,
        message: e.toString(),
      );
    }
  }

  Future<ExportFailure?> share({
    required Uint8List bytes,
    required DocumentDescriptor descriptor,
  }) async {
    if (_shareCompleter != null && !_shareCompleter!.isCompleted) {
      try {
        await _shareCompleter!.future;
      } catch (_) {
        return null;
      }
      return null;
    }

    _shareCompleter = Completer<void>();
    try {
      await _gateway.share(
        bytes: bytes,
        filename: descriptor.filename,
        title: descriptor.title,
      );
      _shareCompleter!.complete();
      return null;
    } on PlatformShareFailure catch (e) {
      _shareCompleter!.complete();
      return ExportFailure(
        operation: ExportOperation.share,
        message: e.message,
      );
    } catch (e) {
      _shareCompleter!.complete();
      return ExportFailure(
        operation: ExportOperation.share,
        message: e.toString(),
      );
    }
  }
}
