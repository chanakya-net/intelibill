import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

class MockSharedPreferences extends Mock implements SharedPreferences {}

void main() {
  late PreferencesStorageImpl preferencesStorage;
  late MockSharedPreferences mockPrefs;

  setUp(() {
    mockPrefs = MockSharedPreferences();
    preferencesStorage = PreferencesStorageImpl(mockPrefs);
  });

  group('PreferencesStorage', () {
    test('setString should call SharedPreferences.setString', () async {
      when(() => mockPrefs.setString(any(), any()))
          .thenAnswer((_) async => true);

      await preferencesStorage.setString('key', 'value');

      verify(() => mockPrefs.setString('key', 'value')).called(1);
    });

    test('getString should call SharedPreferences.getString', () {
      when(() => mockPrefs.getString('key')).thenReturn('value');

      final result = preferencesStorage.getString('key');

      expect(result, 'value');
      verify(() => mockPrefs.getString('key')).called(1);
    });

    test('setBool should call SharedPreferences.setBool', () async {
      when(() => mockPrefs.setBool(any(), any())).thenAnswer((_) async => true);

      await preferencesStorage.setBool(key: 'key', value: true);

      verify(() => mockPrefs.setBool('key', true)).called(1);
    });

    test('remove should call SharedPreferences.remove', () async {
      when(() => mockPrefs.remove(any())).thenAnswer((_) async => true);

      await preferencesStorage.remove('key');

      verify(() => mockPrefs.remove('key')).called(1);
    });
  });
}
