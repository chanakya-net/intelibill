import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/network/auth_interceptor.dart';
import 'package:mocktail/mocktail.dart';

class MockTokenProvider extends Mock implements TokenProvider {}

class MockRequestInterceptorHandler extends Mock
    implements RequestInterceptorHandler {}

void main() {
  late AuthInterceptor authInterceptor;
  late MockTokenProvider mockTokenProvider;
  late MockRequestInterceptorHandler mockHandler;

  setUp(() {
    mockTokenProvider = MockTokenProvider();
    authInterceptor = AuthInterceptor(tokenProvider: mockTokenProvider);
    mockHandler = MockRequestInterceptorHandler();
  });

  group('AuthInterceptor', () {
    test('should add Authorization header if token is present', () async {
      when(() => mockTokenProvider.getAccessToken())
          .thenAnswer((_) async => 'valid_token');
      final options = RequestOptions(path: '');

      await authInterceptor.onRequest(options, mockHandler);

      expect(options.headers['Authorization'], 'Bearer valid_token');
      verify(() => mockHandler.next(options)).called(1);
    });

    test('should NOT add Authorization header if token is null', () async {
      when(() => mockTokenProvider.getAccessToken())
          .thenAnswer((_) async => null);
      final options = RequestOptions(path: '');

      await authInterceptor.onRequest(options, mockHandler);

      expect(options.headers['Authorization'], isNull);
      verify(() => mockHandler.next(options)).called(1);
    });

    test('should NOT add Authorization header if token is empty', () async {
      when(() => mockTokenProvider.getAccessToken())
          .thenAnswer((_) async => '');
      final options = RequestOptions(path: '');

      await authInterceptor.onRequest(options, mockHandler);

      expect(options.headers['Authorization'], isNull);
      verify(() => mockHandler.next(options)).called(1);
    });
  });
}
