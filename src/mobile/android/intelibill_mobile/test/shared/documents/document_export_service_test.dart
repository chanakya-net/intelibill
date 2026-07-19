import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_service.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_gateway.dart';
import 'package:pdf/pdf.dart';

class FakeDocumentOutputGateway implements DocumentOutputGateway {
  FakeDocumentOutputGateway({
    this.shouldFailPrint = false,
    this.shouldFailShare = false,
  });

  final bool shouldFailPrint;
  final bool shouldFailShare;

  int printCallCount = 0;
  int shareCallCount = 0;
  late Uint8List lastPrintBytes;
  late String lastPrintFilename;
  late Uint8List lastShareBytes;
  late String lastShareFilename;
  late String lastShareTitle;

  @override
  Future<void> print({
    required Uint8List bytes,
    required String filename,
  }) async {
    printCallCount++;
    if (shouldFailPrint) {
      throw PlatformPrintFailure(message: 'Fake print error');
    }
    lastPrintBytes = bytes;
    lastPrintFilename = filename;
  }

  @override
  Future<void> share({
    required Uint8List bytes,
    required String filename,
    required String title,
  }) async {
    shareCallCount++;
    if (shouldFailShare) {
      throw PlatformShareFailure(message: 'Fake share error');
    }
    lastShareBytes = bytes;
    lastShareFilename = filename;
    lastShareTitle = title;
  }
}

void main() {
  group('DocumentExportService', () {
    late FakeDocumentOutputGateway gateway;
    late DocumentExportService service;

    setUp(() {
      gateway = FakeDocumentOutputGateway();
      service = DocumentExportService(gateway);
    });

    test('print succeeds and invokes gateway exactly once', () async {
      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      await service.print(bytes: bytes, descriptor: descriptor);

      expect(gateway.printCallCount, 1);
      expect(gateway.lastPrintBytes, bytes);
      expect(gateway.lastPrintFilename, 'po-001.pdf');
    });

    test('share succeeds and invokes gateway exactly once', () async {
      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      await service.share(bytes: bytes, descriptor: descriptor);

      expect(gateway.shareCallCount, 1);
      expect(gateway.lastShareBytes, bytes);
      expect(gateway.lastShareFilename, 'po-001.pdf');
      expect(gateway.lastShareTitle, 'Test PO');
    });

    test('print captures platform failure and exposes typed error', () async {
      gateway = FakeDocumentOutputGateway(shouldFailPrint: true);
      service = DocumentExportService(gateway);

      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      final result = await service.print(bytes: bytes, descriptor: descriptor);

      expect(result, isA<ExportFailure>());
      final failure = result! as ExportFailure;
      expect(failure.operation, ExportOperation.print);
      expect(failure.message, contains('Fake print error'));
    });

    test('share captures platform failure and exposes typed error', () async {
      gateway = FakeDocumentOutputGateway(shouldFailShare: true);
      service = DocumentExportService(gateway);

      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      final result = await service.share(bytes: bytes, descriptor: descriptor);

      expect(result, isA<ExportFailure>());
      final failure = result! as ExportFailure;
      expect(failure.operation, ExportOperation.share);
      expect(failure.message, contains('Fake share error'));
    });

    test(
      'duplicate print taps during flight do not launch another operation',
      () async {
        final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
        const descriptor = DocumentDescriptor(
          title: 'Test PO',
          filename: 'po-001.pdf',
        );

        final future1 = service.print(bytes: bytes, descriptor: descriptor);
        final future2 = service.print(bytes: bytes, descriptor: descriptor);

        final result1 = await future1;
        final result2 = await future2;

        expect(gateway.printCallCount, 1);
        expect(result1, isNull);
        expect(result2, isNull);
      },
    );

    test(
      'duplicate share taps during flight do not launch another operation',
      () async {
        final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
        const descriptor = DocumentDescriptor(
          title: 'Test PO',
          filename: 'po-001.pdf',
        );

        final future1 = service.share(bytes: bytes, descriptor: descriptor);
        final future2 = service.share(bytes: bytes, descriptor: descriptor);

        await Future.wait([future1, future2]);

        expect(gateway.shareCallCount, 1);
      },
    );

    test(
      'print and share can run concurrently without blocking each other',
      () async {
        final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
        const descriptor = DocumentDescriptor(
          title: 'Test PO',
          filename: 'po-001.pdf',
        );

        final printFuture = service.print(bytes: bytes, descriptor: descriptor);
        final shareFuture = service.share(bytes: bytes, descriptor: descriptor);

        await Future.wait([printFuture, shareFuture]);

        expect(gateway.printCallCount, 1);
        expect(gateway.shareCallCount, 1);
      },
    );

    test(
      'concurrent print tap during failed in-flight observes the failure',
      () async {
        gateway = FakeDocumentOutputGateway(shouldFailPrint: true);
        service = DocumentExportService(gateway);

        final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
        const descriptor = DocumentDescriptor(
          title: 'Test PO',
          filename: 'po-001.pdf',
        );

        final future1 = service.print(bytes: bytes, descriptor: descriptor);
        final future2 = service.print(bytes: bytes, descriptor: descriptor);

        final result1 = await future1;
        final result2 = await future2;

        expect(gateway.printCallCount, 1);
        expect(result1, isA<ExportFailure>());
        expect(result2, isA<ExportFailure>());
      },
    );

    test('retry after print failure succeeds on second attempt', () async {
      gateway = FakeDocumentOutputGateway(shouldFailPrint: true);
      service = DocumentExportService(gateway);

      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      var result = await service.print(bytes: bytes, descriptor: descriptor);
      expect(result, isA<ExportFailure>());

      gateway = FakeDocumentOutputGateway();
      service = DocumentExportService(gateway);

      result = await service.print(bytes: bytes, descriptor: descriptor);
      expect(result, isNull);
    });

    test(
      'concurrent share tap during failed in-flight observes the failure',
      () async {
        gateway = FakeDocumentOutputGateway(shouldFailShare: true);
        service = DocumentExportService(gateway);

        final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
        const descriptor = DocumentDescriptor(
          title: 'Test PO',
          filename: 'po-001.pdf',
        );

        final future1 = service.share(bytes: bytes, descriptor: descriptor);
        final future2 = service.share(bytes: bytes, descriptor: descriptor);

        final result1 = await future1;
        final result2 = await future2;

        expect(gateway.shareCallCount, 1);
        expect(result1, isA<ExportFailure>());
        expect(result2, isA<ExportFailure>());
      },
    );

    test('retry after share failure succeeds on second attempt', () async {
      gateway = FakeDocumentOutputGateway(shouldFailShare: true);
      service = DocumentExportService(gateway);

      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      var result = await service.share(bytes: bytes, descriptor: descriptor);
      expect(result, isA<ExportFailure>());

      gateway = FakeDocumentOutputGateway();
      service = DocumentExportService(gateway);

      result = await service.share(bytes: bytes, descriptor: descriptor);
      expect(result, isNull);
    });

    test('build failure returns typed PdfBuildFailure', () async {
      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      final result = await service.buildPdf(
        builder: (_) async => throw Exception('PDF generation failed'),
        descriptor: descriptor,
      );

      expect(result, isA<ExportFailure>());
      final failure = result! as ExportFailure;
      expect(failure.operation, ExportOperation.print);
      expect(failure.message, contains('PDF generation failed'));
    });

    test('build success returns null', () async {
      final bytes = Uint8List.fromList(List<int>.filled(100, 0xFF));
      const descriptor = DocumentDescriptor(
        title: 'Test PO',
        filename: 'po-001.pdf',
      );

      final result = await service.buildPdf(
        builder: (_) async => bytes,
        descriptor: descriptor,
      );

      expect(result, isNull);
    });
  });
}
