import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:shared_preferences/shared_preferences.dart';

part 'locale_controller.g.dart';

const intelibillLocalePreferenceKey = 'intelibillLocale';

const Locale intelibillDefaultLocale = Locale('en', 'IN');

const intelibillSupportedLocales = <Locale>[
  Locale('en', 'IN'),
  Locale('hi', 'IN'),
  Locale('ta', 'IN'),
  Locale('te', 'IN'),
  Locale('bn', 'IN'),
  Locale('ml', 'IN'),
  Locale('kn', 'IN'),
  Locale('mr', 'IN'),
  Locale('gu', 'IN'),
];

@riverpod
Future<PreferencesStorage> localePreferencesStorage(Ref ref) async {
  final prefs = await SharedPreferences.getInstance();
  return PreferencesStorageImpl(prefs);
}

@riverpod
class LocaleController extends _$LocaleController {
  @override
  FutureOr<Locale> build() async {
    final prefs = await ref.watch(localePreferencesStorageProvider.future);
    final savedLocale = prefs.getString(intelibillLocalePreferenceKey);

    return state.value ?? _resolveLocale(savedLocale);
  }

  Future<void> setLocale(Locale locale) async {
    final resolvedLocale = _resolveLocale(_localeTag(locale));
    state = AsyncData(resolvedLocale);
    final prefs = await ref.read(localePreferencesStorageProvider.future);
    await prefs.setString(
      intelibillLocalePreferenceKey,
      _localeTag(resolvedLocale),
    );
  }
}

Locale _resolveLocale(String? value) {
  final locale = _parseLocale(value);
  return locale != null && _isSupportedLocale(locale)
      ? locale
      : intelibillDefaultLocale;
}

Locale? _parseLocale(String? value) {
  if (value == null || value.isEmpty) {
    return null;
  }

  final separator = value.contains('-') ? '-' : '_';
  final parts = value.split(separator);
  if (parts.length != 2) {
    return null;
  }

  final languageCode = parts[0].trim().toLowerCase();
  final countryCode = parts[1].trim().toUpperCase();
  if (languageCode.isEmpty || countryCode.isEmpty) {
    return null;
  }

  return Locale(languageCode, countryCode);
}

bool _isSupportedLocale(Locale locale) {
  return intelibillSupportedLocales.any(
    (supportedLocale) =>
        supportedLocale.languageCode == locale.languageCode &&
        supportedLocale.countryCode == locale.countryCode,
  );
}

String _localeTag(Locale locale) {
  return '${locale.languageCode}-${locale.countryCode}';
}
