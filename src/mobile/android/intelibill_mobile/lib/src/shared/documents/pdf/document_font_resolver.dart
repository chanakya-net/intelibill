import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:pdf/widgets.dart' as pw;

enum PdfScript {
  latin,
  bengali,
  gujarati,
  devanagari,
  kannada,
  malayalam,
  tamil,
  telugu,
}

class PdfFontSet {
  const PdfFontSet({
    required this.regular,
    required this.bold,
    required this.fallback,
  });

  final pw.Font regular;
  final pw.Font bold;
  final List<pw.Font> fallback;
}

class DocumentFontResolver {
  DocumentFontResolver({AssetBundle? bundle}) : _bundle = bundle ?? rootBundle;

  static const licenseAssetPaths = <PdfScript, String>{
    PdfScript.latin: 'assets/licenses/NotoSans-OFL.txt',
    PdfScript.bengali: 'assets/licenses/NotoSansBengali-OFL.txt',
    PdfScript.gujarati: 'assets/licenses/NotoSansGujarati-OFL.txt',
    PdfScript.devanagari: 'assets/licenses/NotoSansDevanagari-OFL.txt',
    PdfScript.kannada: 'assets/licenses/NotoSansKannada-OFL.txt',
    PdfScript.malayalam: 'assets/licenses/NotoSansMalayalam-OFL.txt',
    PdfScript.tamil: 'assets/licenses/NotoSansTamil-OFL.txt',
    PdfScript.telugu: 'assets/licenses/NotoSansTelugu-OFL.txt',
  };

  static const _fontFamilies = <PdfScript, String>{
    PdfScript.latin: 'NotoSans',
    PdfScript.bengali: 'NotoSansBengali',
    PdfScript.gujarati: 'NotoSansGujarati',
    PdfScript.devanagari: 'NotoSansDevanagari',
    PdfScript.kannada: 'NotoSansKannada',
    PdfScript.malayalam: 'NotoSansMalayalam',
    PdfScript.tamil: 'NotoSansTamil',
    PdfScript.telugu: 'NotoSansTelugu',
  };

  final AssetBundle _bundle;
  Future<Map<PdfScript, _FontPair>>? _families;

  static PdfScript scriptFor(Locale locale) {
    return switch (locale.languageCode.toLowerCase()) {
      'bn' => PdfScript.bengali,
      'gu' => PdfScript.gujarati,
      'hi' || 'mr' => PdfScript.devanagari,
      'kn' => PdfScript.kannada,
      'ml' => PdfScript.malayalam,
      'ta' => PdfScript.tamil,
      'te' => PdfScript.telugu,
      _ => PdfScript.latin,
    };
  }

  Future<PdfFontSet> resolve(Locale locale) async {
    final families = await (_families ??= _loadFamilies());
    final primaryScript = scriptFor(locale);
    final primary = families[primaryScript]!;
    final fallback = PdfScript.values
        .where((script) => script != primaryScript)
        .map((script) => families[script]!.regular)
        .toList(growable: false);

    return PdfFontSet(
      regular: primary.regular,
      bold: primary.bold,
      fallback: fallback,
    );
  }

  Future<Map<PdfScript, _FontPair>> _loadFamilies() async {
    final entries = await Future.wait(
      PdfScript.values.map((script) async {
        final family = _fontFamilies[script]!;
        final regular = await _loadFont('assets/fonts/$family-Regular.ttf');
        final bold = await _loadFont('assets/fonts/$family-Bold.ttf');
        return MapEntry(script, _FontPair(regular: regular, bold: bold));
      }),
    );
    return Map<PdfScript, _FontPair>.fromEntries(entries);
  }

  Future<pw.Font> _loadFont(String path) async {
    final data = await _bundle.load(path);
    return pw.Font.ttf(data);
  }
}

class _FontPair {
  const _FontPair({required this.regular, required this.bold});

  final pw.Font regular;
  final pw.Font bold;
}
