import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:printing/printing.dart';

void main() {
  group('DocumentPreviewScaffold', () {
    testWidgets('disables the package preview action bar', (tester) async {
      await tester.pumpWidget(
        _wrap(
          DocumentPreviewScaffold(
            descriptor: _descriptor,
            onBuild: (_) async => Uint8List.fromList([1]),
          ),
        ),
      );

      final preview = tester.widget<PdfPreview>(find.byType(PdfPreview));

      expect(preview.useActions, isFalse);
    });

    testWidgets('disables print and share until the preview build completes', (
      tester,
    ) async {
      final buildCompleter = Completer<Uint8List>();
      var buildCallCount = 0;

      await tester.pumpWidget(
        _wrap(
          DocumentPreviewScaffold(
            descriptor: _descriptor,
            onBuild: (_) {
              buildCallCount++;
              return buildCompleter.future;
            },
            onPrint: (_) async {},
            onShare: (_) async {},
          ),
        ),
      );
      await tester.pump();

      expect(_iconButton(tester, Icons.print).onPressed, isNull);
      expect(_iconButton(tester, Icons.share).onPressed, isNull);

      buildCompleter.complete(Uint8List.fromList([1, 2, 3]));
      await _pumpUntilReady(tester);

      expect(_iconButton(tester, Icons.print).onPressed, isNotNull);
      expect(_iconButton(tester, Icons.share).onPressed, isNotNull);
      expect(buildCallCount, 1);
    });

    testWidgets(
      'forwards the cached preview bytes to print without rebuilding',
      (
        tester,
      ) async {
        var buildCallCount = 0;
        Uint8List? forwarded;
        final bytes = Uint8List.fromList([9, 9, 9]);

        await tester.pumpWidget(
          _wrap(
            DocumentPreviewScaffold(
              descriptor: _descriptor,
              onBuild: (_) async {
                buildCallCount++;
                return bytes;
              },
              onPrint: (b) async {
                forwarded = b;
              },
            ),
          ),
        );
        await _pumpUntilReady(tester);

        await tester.tap(find.byIcon(Icons.print));
        await tester.pump();
        await tester.pump();

        expect(buildCallCount, 1);
        expect(forwarded, same(bytes));
      },
    );

    testWidgets(
      'keeps the preview usable and retryable after a platform export failure',
      (tester) async {
        String? failureMessage;
        var printAttempts = 0;

        await tester.pumpWidget(
          _wrap(
            DocumentPreviewScaffold(
              descriptor: _descriptor,
              onBuild: (_) async => Uint8List.fromList([1]),
              onPrint: (_) async {
                printAttempts++;
                if (printAttempts == 1) {
                  throw Exception('platform failure');
                }
              },
              onFailure: (message) => failureMessage = message,
            ),
          ),
        );
        await _pumpUntilReady(tester);

        await tester.tap(find.byIcon(Icons.print));
        await tester.pump();
        await tester.pump();

        expect(failureMessage, 'Could not print the document. Try again.');
        expect(failureMessage, isNot(contains('platform failure')));
        expect(find.byType(DocumentPreviewScaffold), findsOneWidget);
        expect(_iconButton(tester, Icons.print).onPressed, isNotNull);

        await tester.tap(find.byIcon(Icons.print));
        await tester.pump();
        await tester.pump();

        expect(printAttempts, 2);
      },
    );

    testWidgets(
      'suppresses a rapid duplicate print tap while one is in-flight',
      (
        tester,
      ) async {
        final printCompleter = Completer<void>();
        var printCallCount = 0;

        await tester.pumpWidget(
          _wrap(
            DocumentPreviewScaffold(
              descriptor: _descriptor,
              onBuild: (_) async => Uint8List.fromList([1]),
              onPrint: (_) async {
                printCallCount++;
                await printCompleter.future;
              },
            ),
          ),
        );
        await _pumpUntilReady(tester);

        await tester.tap(find.byIcon(Icons.print));
        await tester.pump();
        await tester.tap(find.byIcon(Icons.print));
        await tester.pump();

        expect(printCallCount, 1);
        printCompleter.complete();
        await tester.pump();
      },
    );

    testWidgets('rebuilds preview and export bytes for a changed document', (
      tester,
    ) async {
      final firstBytes = Uint8List.fromList([1]);
      final secondBytes = Uint8List.fromList([2]);
      Uint8List? forwarded;

      await tester.pumpWidget(
        _previewWith(
          descriptor: _descriptor,
          bytes: firstBytes,
          onPrint: (bytes) async => forwarded = bytes,
        ),
      );
      await _pumpUntilReady(tester);

      await tester.pumpWidget(
        _previewWith(
          descriptor: const DocumentDescriptor(
            title: 'Updated document',
            filename: 'updated-document.pdf',
            pageFormat: DocumentPageFormat.mm80,
          ),
          bytes: secondBytes,
          onPrint: (bytes) async => forwarded = bytes,
        ),
      );
      await _pumpUntilReady(tester);

      await tester.tap(find.byIcon(Icons.print));
      await tester.pump();

      expect(forwarded, same(secondBytes));
    });

    testWidgets('ignores an older in-flight preview build after an update', (
      tester,
    ) async {
      final firstBuild = Completer<Uint8List>();
      final secondBytes = Uint8List.fromList([2]);
      Uint8List? forwarded;

      await tester.pumpWidget(
        _wrap(
          DocumentPreviewScaffold(
            descriptor: _descriptor,
            onBuild: (_) => firstBuild.future,
            onPrint: (bytes) async => forwarded = bytes,
          ),
        ),
      );
      await tester.pump();

      await tester.pumpWidget(
        _previewWith(
          descriptor: const DocumentDescriptor(
            title: 'Updated document',
            filename: 'updated-document.pdf',
            pageFormat: DocumentPageFormat.mm80,
          ),
          bytes: secondBytes,
          onPrint: (bytes) async => forwarded = bytes,
        ),
      );
      await _pumpUntilReady(tester);

      firstBuild.complete(Uint8List.fromList([1]));
      await tester.pump();
      await tester.tap(find.byIcon(Icons.print));
      await tester.pump();

      expect(forwarded, same(secondBytes));
    });
  });
}

const _descriptor = DocumentDescriptor(
  title: 'Document',
  filename: 'document.pdf',
  pageFormat: DocumentPageFormat.mm80,
);

Future<void> _pumpUntilReady(WidgetTester tester) async {
  for (var i = 0; i < 10; i += 1) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

IconButton _iconButton(WidgetTester tester, IconData icon) {
  return tester.widget<IconButton>(
    find.ancestor(of: find.byIcon(icon), matching: find.byType(IconButton)),
  );
}

Widget _wrap(Widget child) => MaterialApp(
  localizationsDelegates: AppLocalizations.localizationsDelegates,
  supportedLocales: AppLocalizations.supportedLocales,
  home: child,
);

Widget _previewWith({
  required DocumentDescriptor descriptor,
  required Uint8List bytes,
  required DocumentActionCallback onPrint,
}) {
  return _wrap(
    DocumentPreviewScaffold(
      descriptor: descriptor,
      onBuild: (_) async => bytes,
      onPrint: onPrint,
    ),
  );
}
