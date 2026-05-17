import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scanner_page.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Wraps the scanner page in a [MaterialApp] with localizations so we can
/// push it from a real [Navigator] and await the popped result.
Widget _buildTestHarness({required ScannerSurfaceBuilder surfaceBuilder}) {
  return MaterialApp(
    locale: const Locale('en', 'IN'),
    supportedLocales: const [Locale('en', 'IN')],
    localizationsDelegates: const [
      AppLocalizations.delegate,
      GlobalMaterialLocalizations.delegate,
      GlobalWidgetsLocalizations.delegate,
      GlobalCupertinoLocalizations.delegate,
    ],
    home: Builder(
      builder: (context) => Scaffold(
        body: TextButton(
          key: const Key('open_scanner'),
          onPressed: () {
            unawaited(
              Navigator.of(context).push<BarcodeScanResult?>(
                MaterialPageRoute<BarcodeScanResult?>(
                  fullscreenDialog: true,
                  builder: (_) =>
                      BarcodeScannerPage(scannerSurfaceBuilder: surfaceBuilder),
                ),
              ),
            );
          },
          child: const Text('Open Scanner'),
        ),
      ),
    ),
  );
}

/// A fake surface builder that exposes a callback to trigger detections.
class _FakeScannerSurface extends StatefulWidget {
  const _FakeScannerSurface({required this.onDetect, required this.callback});

  final void Function(BarcodeCapture) onDetect;
  final void Function(void Function(BarcodeCapture)) callback;

  @override
  State<_FakeScannerSurface> createState() => _FakeScannerSurfaceState();
}

class _FakeScannerSurfaceState extends State<_FakeScannerSurface> {
  @override
  void initState() {
    super.initState();
    widget.callback(widget.onDetect);
  }

  @override
  Widget build(BuildContext context) => const ColoredBox(color: Colors.black);
}

/// Creates a [ScannerSurfaceBuilder] that exposes a trigger function.
///
/// [triggerHolder] will be populated with a function that, when called,
/// simulates a barcode detection.
ScannerSurfaceBuilder _fakeSurface(
  void Function(void Function(BarcodeCapture)) triggerHolder,
) {
  return (context, onDetect) =>
      _FakeScannerSurface(onDetect: onDetect, callback: triggerHolder);
}

BarcodeCapture _makeCapture(
  String value, [
  BarcodeFormat format = BarcodeFormat.ean13,
]) {
  return BarcodeCapture(
    barcodes: [Barcode(rawValue: value, format: format)],
  );
}

BarcodeCapture _makeEmptyCapture() {
  return const BarcodeCapture(barcodes: [Barcode(rawValue: '')]);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

void main() {
  group('BarcodeScannerPage', () {
    testWidgets('renders branded full-screen UI', (tester) async {
      void Function(BarcodeCapture)? trigger;

      await tester.pumpWidget(
        _buildTestHarness(surfaceBuilder: _fakeSurface((t) => trigger = t)),
      );
      await tester.pumpAndSettle();

      // Open the scanner via the harness button
      await tester.tap(find.byKey(const Key('open_scanner')));
      await tester.pumpAndSettle();

      // Title is rendered
      expect(find.text('Scan Barcode'), findsOneWidget);
      // Close button is present
      expect(find.byKey(const Key('barcode_scanner_close')), findsOneWidget);
      // Hint text is shown
      expect(find.byKey(const Key('barcode_scanner_hint')), findsOneWidget);

      // Prevent unused variable warning
      trigger?.call(_makeEmptyCapture());
    });

    testWidgets('close button returns null result', (tester) async {
      BarcodeScanResult? capturedResult = const BarcodeScanResult(
        value: 'sentinel',
      );

      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('en', 'IN'),
          supportedLocales: const [Locale('en', 'IN')],
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                key: const Key('open_scanner'),
                onPressed: () async {
                  capturedResult = await Navigator.of(context)
                      .push<BarcodeScanResult?>(
                        MaterialPageRoute<BarcodeScanResult?>(
                          fullscreenDialog: true,
                          builder: (_) => BarcodeScannerPage(
                            scannerSurfaceBuilder: _fakeSurface((_) {}),
                          ),
                        ),
                      );
                },
                child: const Text('Open Scanner'),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('open_scanner')));
      await tester.pumpAndSettle();

      // Press close
      await tester.tap(find.byKey(const Key('barcode_scanner_close')));
      await tester.pumpAndSettle();

      expect(capturedResult, isNull);
    });

    testWidgets('valid detection returns a BarcodeScanResult', (tester) async {
      void Function(BarcodeCapture)? trigger;
      BarcodeScanResult? capturedResult;

      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('en', 'IN'),
          supportedLocales: const [Locale('en', 'IN')],
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                key: const Key('open_scanner'),
                onPressed: () async {
                  capturedResult = await Navigator.of(context)
                      .push<BarcodeScanResult?>(
                        MaterialPageRoute<BarcodeScanResult?>(
                          fullscreenDialog: true,
                          builder: (_) => BarcodeScannerPage(
                            scannerSurfaceBuilder: _fakeSurface(
                              (t) => trigger = t,
                            ),
                          ),
                        ),
                      );
                },
                child: const Text('Open Scanner'),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('open_scanner')));
      await tester.pumpAndSettle();

      // Simulate a barcode detection
      trigger?.call(_makeCapture('1234567890128'));
      await tester.pump();

      // Success feedback is shown briefly
      expect(find.byKey(const Key('barcode_scanner_success')), findsOneWidget);

      // Let success feedback duration pass
      await tester.pump(const Duration(milliseconds: 700));
      await tester.pumpAndSettle();

      expect(capturedResult, isNotNull);
      expect(capturedResult?.value, equals('1234567890128'));
      expect(capturedResult?.format, equals('ean13'));
    });

    testWidgets('empty rawValue detections are ignored', (tester) async {
      void Function(BarcodeCapture)? trigger;

      await tester.pumpWidget(
        _buildTestHarness(surfaceBuilder: _fakeSurface((t) => trigger = t)),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('open_scanner')));
      await tester.pumpAndSettle();

      // Fire an empty detection
      trigger?.call(_makeEmptyCapture());
      await tester.pump();

      // Still showing hint, not success
      expect(find.byKey(const Key('barcode_scanner_hint')), findsOneWidget);
      expect(find.byKey(const Key('barcode_scanner_success')), findsNothing);
    });

    testWidgets('rapid duplicate detections are debounced', (tester) async {
      void Function(BarcodeCapture)? trigger;
      var popCount = 0;

      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('en', 'IN'),
          supportedLocales: const [Locale('en', 'IN')],
          localizationsDelegates: const [
            AppLocalizations.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                key: const Key('open_scanner'),
                onPressed: () async {
                  await Navigator.of(context).push<BarcodeScanResult?>(
                    MaterialPageRoute<BarcodeScanResult?>(
                      fullscreenDialog: true,
                      builder: (_) => BarcodeScannerPage(
                        scannerSurfaceBuilder: _fakeSurface((t) => trigger = t),
                      ),
                    ),
                  );
                  popCount += 1;
                },
                child: const Text('Open Scanner'),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('open_scanner')));
      await tester.pumpAndSettle();

      // Fire the same barcode value multiple times rapidly
      const duplicateValue = 'DUPE-CODE-001';
      trigger?.call(_makeCapture(duplicateValue, BarcodeFormat.code128));
      await tester.pump();
      trigger?.call(_makeCapture(duplicateValue, BarcodeFormat.code128));
      await tester.pump();
      trigger?.call(_makeCapture(duplicateValue, BarcodeFormat.code128));
      await tester.pump();

      // Let the success feedback pass so the page pops exactly once
      await tester.pump(const Duration(milliseconds: 700));
      await tester.pumpAndSettle();

      // The page was popped exactly once, not three times
      expect(popCount, equals(1));
    });
  });
}
