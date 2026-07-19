import 'dart:typed_data';

abstract class DocumentOutputGateway {
  Future<void> print({
    required Uint8List bytes,
    required String filename,
  });

  Future<void> share({
    required Uint8List bytes,
    required String filename,
    required String title,
  });
}

class PlatformPrintFailure implements Exception {
  PlatformPrintFailure({required this.message});
  final String message;
}

class PlatformShareFailure implements Exception {
  PlatformShareFailure({required this.message});
  final String message;
}
