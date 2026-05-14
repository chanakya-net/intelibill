import 'package:flutter_secure_storage/flutter_secure_storage.dart';

abstract interface class SecureStorage {
  Future<void> saveTokens({required String accessToken, String? refreshToken});
  Future<String?> getAccessToken();
  Future<String?> getRefreshToken();
  Future<void> clearTokens();
  Future<void> write({required String key, required String value});
  Future<String?> read({required String key});
  Future<void> delete({required String key});
  Future<void> deleteAll();
}

class SecureStorageImpl implements SecureStorage {
  SecureStorageImpl({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  static const _accessTokenKey = 'access_token';
  static const _refreshTokenKey = 'refresh_token';

  @override
  Future<void> write({required String key, required String value}) async {
    await _storage.write(key: key, value: value);
  }

  @override
  Future<String?> read({required String key}) {
    return _storage.read(key: key);
  }

  @override
  Future<void> delete({required String key}) async {
    await _storage.delete(key: key);
  }

  @override
  Future<void> deleteAll() async {
    await _storage.deleteAll();
  }

  @override
  Future<void> saveTokens({
    required String accessToken,
    String? refreshToken,
  }) async {
    await write(key: _accessTokenKey, value: accessToken);
    if (refreshToken != null) {
      await write(key: _refreshTokenKey, value: refreshToken);
    }
  }

  @override
  Future<String?> getAccessToken() {
    return read(key: _accessTokenKey);
  }

  @override
  Future<String?> getRefreshToken() {
    return read(key: _refreshTokenKey);
  }

  @override
  Future<void> clearTokens() async {
    await delete(key: _accessTokenKey);
    await delete(key: _refreshTokenKey);
  }
}
