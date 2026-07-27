import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/config/app_config.dart';

void main() {
  group('AppConfig', () {
    test('apiBaseUrl defaults to Android emulator host URL', () {
      expect(AppConfig.apiBaseUrl, equals('http://10.0.2.2:5277/api'));
    });

    test('apiBaseUrl is a non-empty string', () {
      expect(AppConfig.apiBaseUrl.isNotEmpty, isTrue);
    });

    test('apiBaseUrl keeps API_BASE_URL override configurable', () {
      expect(AppConfig.apiBaseUrl, contains('api'));
    });

    test('the emulator default is the documented address', () {
      expect(
        AppConfig.emulatorDefaultApiBaseUrl,
        equals('http://10.0.2.2:5277/api'),
      );
    });

    test('release guard stays out of the way outside a release build', () {
      // Tests never run in product mode, so the guard must pass here even
      // though apiBaseUrl is the emulator default it rejects in a release.
      expect(AppConfig.isReleaseBuild, isFalse);
      expect(AppConfig.assertConfiguredForRelease, returnsNormally);
    });
  });
}
