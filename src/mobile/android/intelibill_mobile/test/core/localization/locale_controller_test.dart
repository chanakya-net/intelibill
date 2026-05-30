import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/locale_controller.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';

class FakePreferencesStorage implements PreferencesStorage {
  final Map<String, Object> _store = <String, Object>{};

  @override
  Future<void> setString(String key, String value) async {
    _store[key] = value;
  }

  @override
  String? getString(String key) {
    final value = _store[key];
    return value is String ? value : null;
  }

  @override
  Future<void> setInt(String key, int value) async {
    _store[key] = value;
  }

  @override
  int? getInt(String key) {
    final value = _store[key];
    return value is int ? value : null;
  }

  @override
  Future<void> setBool({required String key, required bool value}) async {
    _store[key] = value;
  }

  @override
  bool? getBool(String key) {
    final value = _store[key];
    return value is bool ? value : null;
  }

  @override
  Future<void> remove(String key) async {
    _store.remove(key);
  }

  @override
  Future<void> clear() async {
    _store.clear();
  }
}

void main() {
  test('defaults to en-IN when nothing persisted', () async {
    final fakePrefs = FakePreferencesStorage();
    final container = ProviderContainer(
      overrides: [
        localePreferencesStorageProvider.overrideWith((ref) async => fakePrefs),
      ],
    );
    addTearDown(container.dispose);

    final locale = await container.read(localeControllerProvider.future);
    expect(locale, const Locale('en', 'IN'));
  });

  test('loads persisted locale when supported', () async {
    final fakePrefs = FakePreferencesStorage()
      .._store[intelibillLocalePreferenceKey] = 'hi-IN';
    final container = ProviderContainer(
      overrides: [
        localePreferencesStorageProvider.overrideWith((ref) async => fakePrefs),
      ],
    );
    addTearDown(container.dispose);

    final locale = await container.read(localeControllerProvider.future);
    expect(locale, const Locale('hi', 'IN'));
  });

  test('falls back to en-IN when persisted locale invalid', () async {
    final fakePrefs = FakePreferencesStorage()
      .._store[intelibillLocalePreferenceKey] = 'xx-YY';
    final container = ProviderContainer(
      overrides: [
        localePreferencesStorageProvider.overrideWith((ref) async => fakePrefs),
      ],
    );
    addTearDown(container.dispose);

    final locale = await container.read(localeControllerProvider.future);
    expect(locale, const Locale('en', 'IN'));
  });

  test('setLocale persists and updates controller', () async {
    final fakePrefs = FakePreferencesStorage();
    final container = ProviderContainer(
      overrides: [
        localePreferencesStorageProvider.overrideWith((ref) async => fakePrefs),
      ],
    );
    addTearDown(container.dispose);

    await container
        .read(localeControllerProvider.notifier)
        .setLocale(const Locale('ta', 'IN'));

    expect(fakePrefs.getString(intelibillLocalePreferenceKey), 'ta-IN');
    expect(
      container.read(localeControllerProvider).value,
      const Locale('ta', 'IN'),
    );
  });

  test('supported locales matches acceptance list', () {
    expect(intelibillSupportedLocales, const [
      Locale('en', 'IN'),
      Locale('hi', 'IN'),
      Locale('ta', 'IN'),
      Locale('te', 'IN'),
      Locale('bn', 'IN'),
      Locale('ml', 'IN'),
      Locale('kn', 'IN'),
      Locale('mr', 'IN'),
      Locale('gu', 'IN'),
    ]);
  });
}
