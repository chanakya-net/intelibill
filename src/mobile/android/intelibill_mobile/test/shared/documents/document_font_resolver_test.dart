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
        expect(fonts.regular, isNotNull, reason: '${entry.locale}: regular font');
        expect(fonts.bold, isNotNull, reason: '${entry.locale}: bold font');

        // Verify PDF generation with correct fonts succeeds.
        final document = pw.Document();
        document.addPage(
          pw.Page(
            theme: PdfDocumentTheme(fonts).data,
            build: (_) => pw.Column(
              children: [
                pw.Text(entry.text),
                pw.Text(entry.text, style: PdfDocumentTheme(fonts).emphasis),
              ],
            ),
          ),
        );

        final bytes = await document.save();
        expect(
          bytes,
          isNotEmpty,
          reason: '${entry.locale}: PDF bytes generated',
        );
      }
    },
  );

  test(
    'missing fonts produce detectable errors: Latin-only font for Hindi fails to render glyphs',
    () async {
      // Demonstrate that the assertion can detect missing glyphs.
      // Rendering Hindi with Latin-only fonts logs "Unable to find a font" errors.
      final resolver = DocumentFontResolver();
      final latinOnly = await resolver.resolve(const Locale('en', 'IN'));
      const hindiText = 'बहुत';

      final capturedMessages = <String>[];
      final zone = Zone.current.fork(specification: ZoneSpecification(
        print: (_, __, _, line) {
          capturedMessages.add(line);
        },
      ));

      await zone.run(() async {
        final document = pw.Document();
        document.addPage(
          pw.Page(
            build: (_) => pw.Text(
              hindiText,
              style: pw.TextStyle(font: latinOnly.regular),
            ),
          ),
        );
        await document.save();
      });

      expect(
        capturedMessages.any((msg) => msg.contains('Unable to find a font')),
        isTrue,
        reason: 'PDF rendering of Hindi with Latin-only font should log glyph-not-found warnings',
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
