import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

const _locales = <String>{
  'bn',
  'bn_IN',
  'en',
  'en_IN',
  'gu',
  'gu_IN',
  'hi',
  'hi_IN',
  'kn',
  'kn_IN',
  'ml',
  'ml_IN',
  'mr',
  'mr_IN',
  'ta',
  'ta_IN',
  'te',
  'te_IN',
};

final _placeholderPattern = RegExp(
  r'\{([A-Za-z][A-Za-z0-9_]*)(?=\s*[,}])',
);

void main() {
  final arbDirectory = Directory('lib/l10n');
  final files =
      arbDirectory
          .listSync()
          .whereType<File>()
          .where(
            (file) => RegExp(r'app_[a-z]{2}(?:_IN)?\.arb$').hasMatch(file.path),
          )
          .toList()
        ..sort((left, right) => left.path.compareTo(right.path));

  final catalogs = <String, Map<String, Object?>>{
    for (final file in files) _localeOf(file): _readArb(file),
  };
  final template = catalogs['en_IN']!;
  final templateKeys = _scopedKeys(template);

  test('discovers every supported base and India-region ARB', () {
    expect(catalogs.keys, unorderedEquals(_locales));
    expect(files, hasLength(18));
  });

  test('template covers every purchase-order workflow and failure group', () {
    final groups = <String, bool Function(String)>{
      'directory': (key) => key.startsWith('purchaseOrders'),
      'builder': (key) =>
          key.startsWith('purchaseOrderBuilder') ||
          key.startsWith('purchaseOrderDraft'),
      'lifecycle': (key) =>
          key.startsWith('purchaseOrderStatus') ||
          key.startsWith('purchaseOrderPlace') ||
          key.startsWith('purchaseOrderCancel') ||
          key.startsWith('purchaseOrderClose'),
      'receiving': (key) => key.startsWith('purchaseOrderReceive'),
      'documents': (key) =>
          key.startsWith('purchaseOrderDocument') ||
          key.startsWith('documentOutput'),
      'failure': (key) =>
          key.startsWith('purchaseOrderFailure') ||
          key.startsWith('documentOutputFailure'),
    };

    for (final MapEntry(key: name, value: matches) in groups.entries) {
      expect(
        templateKeys.where(matches),
        isNotEmpty,
        reason: 'app_en_IN.arb has no $name messages',
      );
    }
  });

  test('all catalogs have exact scoped key and placeholder parity', () {
    for (final MapEntry(key: locale, value: catalog) in catalogs.entries) {
      expect(
        _scopedKeys(catalog),
        templateKeys,
        reason: '$locale scoped keys differ from en_IN',
      );

      for (final key in templateKeys) {
        expect(
          _placeholders(catalog[key]! as String),
          _placeholders(template[key]! as String),
          reason: '$locale:$key placeholders differ from en_IN',
        );
      }
    }
  });

  test('template declares valid types for every placeholder', () {
    const supportedTypes = {'String', 'int', 'double', 'num', 'DateTime'};

    for (final key in templateKeys) {
      final placeholders = _placeholders(template[key]! as String);
      if (placeholders.isEmpty) continue;

      final metadata = template['@$key'];
      expect(metadata, isA<Map<String, Object?>>(), reason: '@$key missing');
      final metadataMap = metadata! as Map<String, Object?>;
      final definitions = metadataMap['placeholders'];
      expect(
        definitions,
        isA<Map<String, Object?>>(),
        reason: '@$key placeholders missing',
      );
      final definitionMap = definitions! as Map<String, Object?>;
      expect(
        definitionMap.keys.toSet(),
        placeholders,
        reason: '@$key mismatch',
      );
      for (final placeholder in placeholders) {
        final definition = definitionMap[placeholder]! as Map<String, Object?>;
        expect(
          definition['type'],
          isIn(supportedTypes),
          reason: '@$key:$placeholder has an invalid type',
        );
      }
    }
  });
}

String _localeOf(File file) => RegExp(
  r'app_([a-z]{2}(?:_IN)?)\.arb$',
).firstMatch(file.path)!.group(1)!;

Map<String, Object?> _readArb(File file) =>
    (jsonDecode(file.readAsStringSync()) as Map<String, dynamic>)
        .cast<String, Object?>();

Set<String> _scopedKeys(Map<String, Object?> catalog) => catalog.keys
    .where(
      (key) =>
          !key.startsWith('@') &&
          (key.startsWith('purchaseOrder') ||
              key.startsWith('purchaseOrders') ||
              key.startsWith('documentOutput')),
    )
    .toSet();

Set<String> _placeholders(String message) => _placeholderPattern
    .allMatches(message)
    .map((match) => match.group(1)!)
    .toSet();
