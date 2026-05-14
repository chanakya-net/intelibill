import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/config/app_config.dart';

void main() {
  group('AppConfig', () {
    test('apiBaseUrl defaults to http://10.0.2.2:5277/api', () {
      expect(
        AppConfig.apiBaseUrl,
        equals('http://10.0.2.2:5277/api'),
      );
    });

    test('apiBaseUrl is a non-empty string', () {
      expect(AppConfig.apiBaseUrl.isNotEmpty, isTrue);
    });

    test('apiBaseUrl has valid format', () {
      expect(AppConfig.apiBaseUrl, contains('http'));
    });
  });
}
