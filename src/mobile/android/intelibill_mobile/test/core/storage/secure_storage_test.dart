import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:mocktail/mocktail.dart';

class MockFlutterSecureStorage extends Mock implements FlutterSecureStorage {}

void main() {
  late SecureStorageImpl secureStorage;
  late MockFlutterSecureStorage mockStorage;

  setUp(() {
    mockStorage = MockFlutterSecureStorage();
    secureStorage = SecureStorageImpl(storage: mockStorage);
  });

  group('SecureStorage', () {
    test('saveTokens should write both access and refresh tokens', () async {
      when(
        () => mockStorage.write(
          key: any(named: 'key'),
          value: any(named: 'value'),
        ),
      ).thenAnswer((_) async {});

      await secureStorage.saveTokens(
        accessToken: 'access',
        refreshToken: 'refresh',
      );

      verify(
        () => mockStorage.write(key: 'access_token', value: 'access'),
      ).called(1);
      verify(
        () => mockStorage.write(key: 'refresh_token', value: 'refresh'),
      ).called(1);
    });

    test(
      'saveTokens should clear refresh token when refreshToken is null',
      () async {
        when(
          () => mockStorage.write(
            key: any(named: 'key'),
            value: any(named: 'value'),
          ),
        ).thenAnswer((_) async {});
        when(
          () => mockStorage.delete(key: any(named: 'key')),
        ).thenAnswer((_) async {});

        await secureStorage.saveTokens(accessToken: 'access');

        verify(
          () => mockStorage.write(key: 'access_token', value: 'access'),
        ).called(1);
        verify(() => mockStorage.delete(key: 'refresh_token')).called(1);
        verifyNever(
          () => mockStorage.write(
            key: 'refresh_token',
            value: any(named: 'value'),
          ),
        );
      },
    );

    test('getAccessToken should read from storage', () async {
      when(
        () => mockStorage.read(key: 'access_token'),
      ).thenAnswer((_) async => 'some_token');

      final token = await secureStorage.getAccessToken();

      expect(token, 'some_token');
      verify(() => mockStorage.read(key: 'access_token')).called(1);
    });

    test('clearTokens should delete both tokens', () async {
      when(
        () => mockStorage.delete(key: any(named: 'key')),
      ).thenAnswer((_) async {});

      await secureStorage.clearTokens();

      verify(() => mockStorage.delete(key: 'access_token')).called(1);
      verify(() => mockStorage.delete(key: 'refresh_token')).called(1);
    });
  });
}
