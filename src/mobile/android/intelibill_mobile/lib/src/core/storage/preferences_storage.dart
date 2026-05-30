import 'package:shared_preferences/shared_preferences.dart';

abstract interface class PreferencesStorage {
  Future<void> setString(String key, String value);
  String? getString(String key);
  Future<void> setInt(String key, int value);
  int? getInt(String key);
  Future<void> setBool({required String key, required bool value});
  bool? getBool(String key);
  Future<void> remove(String key);
  Future<void> clear();
}

class PreferencesStorageImpl implements PreferencesStorage {
  PreferencesStorageImpl(this._prefs);

  final SharedPreferences _prefs;

  @override
  Future<void> setString(String key, String value) async {
    await _prefs.setString(key, value);
  }

  @override
  String? getString(String key) {
    return _prefs.getString(key);
  }

  @override
  Future<void> setInt(String key, int value) async {
    await _prefs.setInt(key, value);
  }

  @override
  int? getInt(String key) {
    return _prefs.getInt(key);
  }

  @override
  Future<void> setBool({required String key, required bool value}) async {
    await _prefs.setBool(key, value);
  }

  @override
  bool? getBool(String key) {
    return _prefs.getBool(key);
  }

  @override
  Future<void> remove(String key) async {
    await _prefs.remove(key);
  }

  @override
  Future<void> clear() async {
    await _prefs.clear();
  }
}
