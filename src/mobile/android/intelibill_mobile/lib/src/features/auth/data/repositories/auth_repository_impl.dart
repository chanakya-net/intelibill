import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:intelibill_mobile/src/features/auth/data/data_sources/auth_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/auth/data/mappers/auth_mapper.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/domain/repositories/auth_repository.dart';

class AuthRepositoryImpl implements AuthRepository {
  AuthRepositoryImpl({
    required AuthRemoteDataSource remoteDataSource,
    required SecureStorage secureStorage,
    required PreferencesStorage preferencesStorage,
  }) : _remoteDataSource = remoteDataSource,
       _secureStorage = secureStorage,
       _preferencesStorage = preferencesStorage;

  final AuthRemoteDataSource _remoteDataSource;
  final SecureStorage _secureStorage;
  final PreferencesStorage _preferencesStorage;

  static const _rememberedIdentifierKey = 'remembered_identifier';

  @override
  Future<AuthSession> login({
    required String identifier,
    required String password,
    required bool rememberMe,
  }) async {
    try {
      final dto = await _remoteDataSource.login(
        identifier: identifier,
        password: password,
      );

      final session = AuthMapper.toDomain(dto, rememberMe: rememberMe);

      // Persist tokens
      await _secureStorage.saveTokens(
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      );

      // Save remembered identifier if requested
      if (rememberMe) {
        await saveRememberedIdentifier(identifier: identifier);
      } else {
        await clearRememberedIdentifier();
      }

      return session;
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }

  @override
  Future<AuthSession> updateProfile({
    required String email,
    String? phoneNumber,
    required String firstName,
    required String lastName,
    required String language,
  }) async {
    try {
      final dto = await _remoteDataSource.updateProfile(
        email: email,
        phoneNumber: phoneNumber,
        firstName: firstName,
        lastName: lastName,
        language: language,
      );
      final session = AuthMapper.toDomain(dto);

      // Persist new tokens
      await _secureStorage.saveTokens(
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      );

      return session;
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }

  @override
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    try {
      await _remoteDataSource.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      );
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }

  @override
  Future<AuthSession> switchShop({required String shopId}) async {
    try {
      final dto = await _remoteDataSource.setDefaultShop(shopId: shopId);
      final session = AuthMapper.toDomain(dto);

      // Persist new tokens
      await _secureStorage.saveTokens(
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      );

      return session;
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }

  @override
  Future<AuthSession> refreshToken({required String refreshToken}) async {
    try {
      final dto = await _remoteDataSource.refreshToken(
        refreshToken: refreshToken,
      );
      final session = AuthMapper.toDomain(dto);

      // Persist new tokens
      await _secureStorage.saveTokens(
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      );

      return session;
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }

  @override
  Future<void> revokeToken({required String refreshToken}) async {
    try {
      await _remoteDataSource.revokeToken(refreshToken: refreshToken);
    } on AppException {
      rethrow;
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    } finally {
      await clearTokens();
    }
  }

  @override
  Future<void> clearTokens() async {
    await _secureStorage.clearTokens();
  }

  @override
  Future<String?> getAccessToken() {
    return _secureStorage.getAccessToken();
  }

  @override
  Future<String?> getRefreshToken() {
    return _secureStorage.getRefreshToken();
  }

  @override
  Future<void> saveRememberedIdentifier({required String identifier}) async {
    await _preferencesStorage.setString(_rememberedIdentifierKey, identifier);
  }

  @override
  Future<String?> getRememberedIdentifier() async {
    return _preferencesStorage.getString(_rememberedIdentifierKey);
  }

  @override
  Future<void> clearRememberedIdentifier() async {
    await _preferencesStorage.remove(_rememberedIdentifierKey);
  }
}
