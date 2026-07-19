import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf/document_font_resolver.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_formatters.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_theme.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:pdf/widgets.dart' as pw;

Future<void> main() async {
  TestWidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting();
  final localesAndText = [
    (locale: const Locale('en', 'IN'), text: 'Inventory'),
    (locale: const Locale('bn', 'IN'), text: 'বাংলা'),
    (locale: const Locale('gu', 'IN'), text: 'ગુજરાતી'),
    (locale: const Locale('hi', 'IN'), text: 'हिन्दी'),
    (locale: const Locale('mr', 'IN'), text: 'मराठी'),
    (locale: const Locale('kn', 'IN'), text: 'ಕನ್ನಡ'),
    (locale: const Locale('ml', 'IN'), text: 'മലയാളം'),
    (locale: const Locale('ta', 'IN'), text: 'தமிழ்'),
    (locale: const Locale('te', 'IN'), text: 'తెలుగు'),
  ];

  test('resolves each supported locale to a deterministic script', () {
    expect(
      DocumentFontResolver.scriptFor(const Locale('en', 'IN')),
      PdfScript.latin,
    );
    expect(
      DocumentFontResolver.scriptFor(const Locale('mr', 'IN')),
      PdfScript.devanagari,
    );
    expect(
      DocumentFontResolver.scriptFor(const Locale('bn', 'IN')),
      PdfScript.bengali,
    );
    expect(
      DocumentFontResolver.scriptFor(const Locale('unknown', 'IN')),
      PdfScript.latin,
    );
  });

  test(
    'bundled fonts successfully render representative text for all supported scripts',
    () async {
      final resolver = DocumentFontResolver();

      for (final entry in localesAndText) {
        // Verify font resolution succeeds (fonts are bundled and loadable).
        final fonts = await resolver.resolve(entry.locale);
        expect(
          fonts.regular,
          isNotNull,
          reason: '${entry.locale}: regular font',
        );
        expect(fonts.bold, isNotNull, reason: '${entry.locale}: bold font');

        final result = await _renderRepresentativeText(
          fonts: fonts,
          text: entry.text,
        );
        expect(
          result.bytes,
          isNotEmpty,
          reason: '${entry.locale}: PDF bytes generated',
        );
        expect(
          result.hasMissingGlyphDiagnostic,
          isFalse,
          reason:
              '${entry.locale}: normal and bold text have no missing glyphs',
        );
      }
    },
  );

  test(
    'missing fonts produce detectable errors: Latin-only font for Hindi fails to render glyphs',
    () async {
      final resolver = DocumentFontResolver();
      final latinOnly = await resolver.resolve(const Locale('en', 'IN'));
      final result = await _renderRepresentativeText(
        fonts: PdfFontSet(
          regular: latinOnly.regular,
          bold: latinOnly.bold,
          fallback: const [],
        ),
        text: 'बहुत',
      );
      expect(
        result.hasMissingGlyphDiagnostic,
        isTrue,
        reason:
            'PDF rendering of Hindi with Latin-only font should log glyph-not-found warnings',
      );
    },
  );

  test('PDF formatters use the active locale', () {
    final date = DateTime(2026, 7, 19);
    const bengali = Locale('bn', 'IN');

    expect(formatPdfDate(date, locale: bengali), isNot(formatPdfDate(date)));
    expect(
      formatPdfDecimal(12345.6, locale: bengali),
      isNot(formatPdfDecimal(12345.6)),
    );
    expect(
      formatPdfPercentage(12.5, locale: bengali),
      isNot(formatPdfPercentage(12.5)),
    );
    expect(
      formatPdfInr(12345.6),
      contains('₹'),
    );
  });

  test(
    'font loading has no runtime download path and every family is licensed',
    () {
      final source = File(
        'lib/src/shared/documents/pdf/document_font_resolver.dart',
      ).readAsStringSync();
      expect(source, isNot(contains('PdfGoogleFonts')));
      expect(source, isNot(contains('http://')));
      expect(source, isNot(contains('https://')));

      for (final path in DocumentFontResolver.licenseAssetPaths.values) {
        expect(File(path).existsSync(), isTrue);
      }
    },
  );
}

Future<_RenderResult> _renderRepresentativeText({
  required PdfFontSet fonts,
  required String text,
}) async {
  final capturedMessages = <String>[];
  final result = await Zone.current
      .fork(
        specification: ZoneSpecification(
          print: (_, _, _, line) => capturedMessages.add(line),
        ),
      )
      .run(() async {
        final document = pw.Document();
        final theme = PdfDocumentTheme(fonts);
        document.addPage(
          pw.Page(
            theme: theme.data,
            build: (_) => pw.Column(
              children: [
                pw.Text(text),
                pw.Text(text, style: theme.emphasis),
              ],
            ),
          ),
        );
        return document.save();
      });

  return _RenderResult(bytes: result, messages: capturedMessages);
}

class _RenderResult {
  const _RenderResult({required this.bytes, required this.messages});

  final List<int> bytes;
  final List<String> messages;

  bool get hasMissingGlyphDiagnostic =>
      messages.any((message) => message.contains('Unable to find a font'));
}
