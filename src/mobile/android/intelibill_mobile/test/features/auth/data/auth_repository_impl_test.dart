import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:intelibill_mobile/src/features/auth/data/data_sources/auth_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_user_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/repositories/auth_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockAuthRemoteDataSource extends Mock implements AuthRemoteDataSource {}

class MockSecureStorage extends Mock implements SecureStorage {}

class MockPreferencesStorage extends Mock implements PreferencesStorage {}

void main() {
  late MockAuthRemoteDataSource remoteDataSource;
  late MockSecureStorage secureStorage;
  late MockPreferencesStorage preferencesStorage;
  late AuthRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockAuthRemoteDataSource();
    secureStorage = MockSecureStorage();
    preferencesStorage = MockPreferencesStorage();
    repository = AuthRepositoryImpl(
      remoteDataSource: remoteDataSource,
      secureStorage: secureStorage,
      preferencesStorage: preferencesStorage,
    );
  });

  group('AuthRepositoryImpl', () {
    test('login calls remote data source and persists tokens', () async {
      final mockDto = AuthResultDto(
        accessToken: 'access_token',
        refreshToken: 'refresh_token',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 14, 10),
        user: const AuthUserDto(
          id: 'user-1',
          firstName: 'John',
          lastName: 'Doe',
        ),
      );

      when(
        () => remoteDataSource.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
        ),
      ).thenAnswer((_) async => mockDto);

      when(
        () => secureStorage.saveTokens(
          accessToken: any(named: 'accessToken'),
          refreshToken: any(named: 'refreshToken'),
        ),
      ).thenAnswer((_) async => Future.value());

      when(
        () => preferencesStorage.setString(any(), any()),
      ).thenAnswer((_) async => Future.value());

      final result = await repository.login(
        identifier: 'test@example.com',
        password: 'password123',
        rememberMe: true,
      );

      expect(result.accessToken, 'access_token');
      expect(result.rememberMe, true);
      verify(
        () => secureStorage.saveTokens(
          accessToken: 'access_token',
          refreshToken: 'refresh_token',
        ),
      ).called(1);
      verify(
        () => preferencesStorage.setString(any(), 'test@example.com'),
      ).called(1);
    });

    test('login without rememberMe clears any saved identifier', () async {
      final mockDto = AuthResultDto(
        accessToken: 'access_token',
        refreshToken: 'refresh_token',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 14, 10),
        user: const AuthUserDto(
          id: 'user-1',
          firstName: 'John',
          lastName: 'Doe',
        ),
      );

      when(
        () => remoteDataSource.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
        ),
      ).thenAnswer((_) async => mockDto);

      when(
        () => secureStorage.saveTokens(
          accessToken: any(named: 'accessToken'),
          refreshToken: any(named: 'refreshToken'),
        ),
      ).thenAnswer((_) async => Future.value());

      when(
        () => preferencesStorage.remove(any()),
      ).thenAnswer((_) async => Future.value());

      await repository.login(
        identifier: 'test@example.com',
        password: 'password123',
        rememberMe: false,
      );

      verifyNever(() => preferencesStorage.setString(any(), any()));
      verify(() => preferencesStorage.remove(any())).called(1);
    });

    test('refreshToken updates tokens and clears identifier', () async {
      final mockDto = AuthResultDto(
        accessToken: 'new_access_token',
        refreshToken: 'new_refresh_token',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 14, 10),
        user: const AuthUserDto(
          id: 'user-1',
          firstName: 'John',
          lastName: 'Doe',
        ),
      );

      when(
        () => remoteDataSource.refreshToken(
          refreshToken: any(named: 'refreshToken'),
        ),
      ).thenAnswer((_) async => mockDto);

      when(
        () => secureStorage.saveTokens(
          accessToken: any(named: 'accessToken'),
          refreshToken: any(named: 'refreshToken'),
        ),
      ).thenAnswer((_) async => Future.value());

      final result = await repository.refreshToken(refreshToken: 'old_refresh');

      expect(result.accessToken, 'new_access_token');
      verify(
        () => secureStorage.saveTokens(
          accessToken: 'new_access_token',
          refreshToken: 'new_refresh_token',
        ),
      ).called(1);
    });

    test('revokeToken clears all tokens', () async {
      when(
        () => remoteDataSource.revokeToken(
          refreshToken: any(named: 'refreshToken'),
        ),
      ).thenAnswer((_) async => Future.value());

      when(
        () => secureStorage.clearTokens(),
      ).thenAnswer((_) async => Future.value());

      await repository.revokeToken(refreshToken: 'token_to_revoke');

      verify(() => secureStorage.clearTokens()).called(1);
    });

    test('revokeToken clears tokens even when revoke fails', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'Connection failed'),
      );

      when(
        () => remoteDataSource.revokeToken(
          refreshToken: any(named: 'refreshToken'),
        ),
      ).thenThrow(exception);

      when(
        () => secureStorage.clearTokens(),
      ).thenAnswer((_) async => Future.value());

      expect(
        () => repository.revokeToken(refreshToken: 'token_to_revoke'),
        throwsA(same(exception)),
      );

      verify(() => secureStorage.clearTokens()).called(1);
    });

    test('clearTokens removes all stored tokens', () async {
      when(
        () => secureStorage.clearTokens(),
      ).thenAnswer((_) async => Future.value());

      await repository.clearTokens();

      verify(() => secureStorage.clearTokens()).called(1);
    });

    test('getAccessToken retrieves token from secure storage', () async {
      when(
        () => secureStorage.getAccessToken(),
      ).thenAnswer((_) async => 'access_token');

      final token = await repository.getAccessToken();

      expect(token, 'access_token');
    });

    test('getRefreshToken retrieves token from secure storage', () async {
      when(
        () => secureStorage.getRefreshToken(),
      ).thenAnswer((_) async => 'refresh_token');

      final token = await repository.getRefreshToken();

      expect(token, 'refresh_token');
    });

    test('saveRememberedIdentifier saves to preferences', () async {
      when(
        () => preferencesStorage.setString(any(), any()),
      ).thenAnswer((_) async => Future.value());

      await repository.saveRememberedIdentifier(identifier: 'test@example.com');

      verify(
        () => preferencesStorage.setString(any(), 'test@example.com'),
      ).called(1);
    });

    test('getRememberedIdentifier retrieves from preferences', () async {
      when(
        () => preferencesStorage.getString(any()),
      ).thenReturn('test@example.com');

      final identifier = await repository.getRememberedIdentifier();

      expect(identifier, 'test@example.com');
    });

    test('clearRememberedIdentifier removes from preferences', () async {
      when(
        () => preferencesStorage.remove(any()),
      ).thenAnswer((_) async => Future.value());

      await repository.clearRememberedIdentifier();

      verify(() => preferencesStorage.remove(any())).called(1);
    });

    test('login propagates network errors as AppException', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'Connection failed'),
      );

      when(
        () => remoteDataSource.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
        ),
      ).thenThrow(exception);

      expect(
        repository.login(
          identifier: 'test@example.com',
          password: 'password123',
          rememberMe: false,
        ),
        throwsA(same(exception)),
      );
    });
  });
}
