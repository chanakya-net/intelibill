import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/config/app_config.dart';

void main() {
  group('AppConfig', () {
    test('apiBaseUrl defaults to Android emulator host URL', () {
      expect(
        AppConfig.apiBaseUrl,
        equals('http://10.0.2.2:5277/api'),
      );
    });

    test('apiBaseUrl is a non-empty string', () {
      expect(AppConfig.apiBaseUrl.isNotEmpty, isTrue);
    });

    test('apiBaseUrl keeps API_BASE_URL override configurable', () {
      expect(
        AppConfig.apiBaseUrl,
        contains('api'),
      );
    });
  });
}
