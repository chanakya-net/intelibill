import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/network/auth_interceptor.dart';
import 'package:mocktail/mocktail.dart';

class MockAccessTokenProvider extends Mock {
  Future<String?> call();
}

class MockRefreshTokenProvider extends Mock {
  Future<String?> call();
}

class MockTokenRefresher extends Mock {
  Future<void> call();
}

class MockTokenSaver extends Mock {
  Future<void> call();
}

class MockAuthStateClearer extends Mock {
  Future<void> call();
}

class MockRequestInterceptorHandler extends Mock
    implements RequestInterceptorHandler {}

class MockErrorInterceptorHandler extends Mock
    implements ErrorInterceptorHandler {}

class MockDio extends Mock implements Dio {}

void main() {
  late AuthInterceptor authInterceptor;
  late MockAccessTokenProvider mockGetAccessToken;
  late MockRefreshTokenProvider mockGetRefreshToken;
  late MockTokenRefresher mockRefreshTokens;
  late MockTokenSaver mockSaveRefreshedTokens;
  late MockAuthStateClearer mockClearAuthState;
  late MockRequestInterceptorHandler mockRequestHandler;
  late MockErrorInterceptorHandler mockErrorHandler;
  late MockDio mockDio;

  setUp(() {
    mockGetAccessToken = MockAccessTokenProvider();
    mockGetRefreshToken = MockRefreshTokenProvider();
    mockRefreshTokens = MockTokenRefresher();
    mockSaveRefreshedTokens = MockTokenSaver();
    mockClearAuthState = MockAuthStateClearer();
    mockRequestHandler = MockRequestInterceptorHandler();
    mockErrorHandler = MockErrorInterceptorHandler();
    mockDio = MockDio();
    when(() => mockClearAuthState()).thenAnswer((_) async {});
    when(() => mockGetRefreshToken()).thenAnswer((_) async => 'refresh_token');

    authInterceptor = AuthInterceptor(
      getAccessToken: mockGetAccessToken.call,
      getRefreshToken: mockGetRefreshToken.call,
      refreshTokens: mockRefreshTokens.call,
      saveRefreshedTokens: mockSaveRefreshedTokens.call,
      clearAuthState: mockClearAuthState.call,
      dio: mockDio,
    );
  });

  group('AuthInterceptor.onRequest', () {
    test(
      'should add Authorization header with Bearer token when token exists',
      () async {
        when(
          () => mockGetAccessToken(),
        ).thenAnswer((_) async => 'valid_access_token');

        final options = RequestOptions(path: '/api/items');

        await authInterceptor.onRequest(options, mockRequestHandler);

        expect(options.headers['Authorization'], 'Bearer valid_access_token');
        verify(() => mockRequestHandler.next(options)).called(1);
      },
    );

    test('should NOT add Authorization header when token is null', () async {
      when(() => mockGetAccessToken()).thenAnswer((_) async => null);

      final options = RequestOptions(path: '/api/items');

      await authInterceptor.onRequest(options, mockRequestHandler);

      expect(options.headers['Authorization'], isNull);
      verify(() => mockRequestHandler.next(options)).called(1);
    });

    test('should NOT add Authorization header when token is empty', () async {
      when(() => mockGetAccessToken()).thenAnswer((_) async => '');

      final options = RequestOptions(path: '/api/items');

      await authInterceptor.onRequest(options, mockRequestHandler);

      expect(options.headers['Authorization'], isNull);
      verify(() => mockRequestHandler.next(options)).called(1);
    });
  });

  group('AuthInterceptor.onError', () {
    test('should skip non-401 errors', () async {
      final response = Response<dynamic>(
        requestOptions: RequestOptions(path: '/api/items'),
        statusCode: 500,
      );
      final dioException = DioException(
        requestOptions: RequestOptions(path: '/api/items'),
        response: response,
        type: DioExceptionType.badResponse,
      );

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockErrorHandler.next(dioException)).called(1);
      verifyNever(() => mockRefreshTokens());
      verifyNever(() => mockGetRefreshToken());
    });

    test('should skip 401 on login endpoint', () async {
      final response = Response<dynamic>(
        requestOptions: RequestOptions(path: '/auth/login'),
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: RequestOptions(path: '/auth/login'),
        response: response,
        type: DioExceptionType.badResponse,
      );

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockErrorHandler.next(dioException)).called(1);
      verifyNever(() => mockRefreshTokens());
      verifyNever(() => mockGetRefreshToken());
    });

    test('should skip 401 on refresh endpoint', () async {
      final response = Response<dynamic>(
        requestOptions: RequestOptions(path: '/auth/token/refresh'),
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: RequestOptions(path: '/auth/token/refresh'),
        response: response,
        type: DioExceptionType.badResponse,
      );

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockErrorHandler.next(dioException)).called(1);
      verifyNever(() => mockRefreshTokens());
      verifyNever(() => mockGetRefreshToken());
    });

    test('should skip 401 on revoke endpoint', () async {
      final response = Response<dynamic>(
        requestOptions: RequestOptions(path: '/auth/token/revoke'),
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: RequestOptions(path: '/auth/token/revoke'),
        response: response,
        type: DioExceptionType.badResponse,
      );

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockErrorHandler.next(dioException)).called(1);
      verifyNever(() => mockRefreshTokens());
      verifyNever(() => mockGetRefreshToken());
    });

    test('should clear auth state on second 401 (already retried)', () async {
      final options = RequestOptions(path: '/api/items');
      options.extra['_auth_retry_attempted'] = true;

      final response = Response<dynamic>(
        requestOptions: options,
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      );

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockClearAuthState()).called(1);
      verify(() => mockErrorHandler.next(dioException)).called(1);
      verifyNever(() => mockRefreshTokens());
      verifyNever(() => mockGetRefreshToken());
    });

    test('should refresh and retry on 401 with eligible request', () async {
      final options = RequestOptions(
        path: '/api/items',
        method: 'GET',
        headers: {},
      );

      final response = Response<dynamic>(
        requestOptions: options,
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      );

      when(() => mockRefreshTokens()).thenAnswer((_) async {});
      when(() => mockSaveRefreshedTokens()).thenAnswer((_) async {});
      when(
        () => mockGetRefreshToken(),
      ).thenAnswer((_) async => 'refresh_token');
      when(
        () => mockGetAccessToken(),
      ).thenAnswer((_) async => 'new_access_token');

      final retryResponse = Response<dynamic>(
        requestOptions: options,
        statusCode: 200,
        data: {'success': true},
      );

      when(
        () => mockDio.request<dynamic>(
          any(),
          data: any(named: 'data'),
          queryParameters: any(named: 'queryParameters'),
          options: any(named: 'options'),
          cancelToken: any(named: 'cancelToken'),
        ),
      ).thenAnswer((_) async => retryResponse);

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockRefreshTokens()).called(1);
      verify(() => mockGetRefreshToken()).called(1);
      verify(() => mockSaveRefreshedTokens()).called(1);
      verify(() => mockGetAccessToken()).called(1);
      verify(() => mockErrorHandler.resolve(retryResponse)).called(1);
      verifyNever(() => mockClearAuthState());
    });

    test(
      'should clear auth state when getAccessToken returns null after refresh',
      () async {
        final options = RequestOptions(path: '/api/items');

        final response = Response<dynamic>(
          requestOptions: options,
          statusCode: 401,
        );
        final dioException = DioException(
          requestOptions: options,
          response: response,
          type: DioExceptionType.badResponse,
        );

        when(() => mockRefreshTokens()).thenAnswer((_) async {});
        when(() => mockSaveRefreshedTokens()).thenAnswer((_) async {});
        when(
          () => mockGetRefreshToken(),
        ).thenAnswer((_) async => 'refresh_token');
        when(() => mockGetAccessToken()).thenAnswer((_) async => null);

        await authInterceptor.onError(dioException, mockErrorHandler);

        verify(() => mockClearAuthState()).called(1);
        verify(() => mockErrorHandler.next(dioException)).called(1);
      },
    );

    test('should clear auth state when refresh throws exception', () async {
      final options = RequestOptions(path: '/api/items');

      final response = Response<dynamic>(
        requestOptions: options,
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      );

      when(() => mockRefreshTokens()).thenThrow(Exception('Refresh failed'));
      when(
        () => mockGetRefreshToken(),
      ).thenAnswer((_) async => 'refresh_token');

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockClearAuthState()).called(1);
      verify(() => mockErrorHandler.next(dioException)).called(1);
    });

    test('should clear auth state when retry request fails', () async {
      final options = RequestOptions(
        path: '/api/items',
        method: 'GET',
        headers: {},
      );

      final response = Response<dynamic>(
        requestOptions: options,
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      );

      when(() => mockRefreshTokens()).thenAnswer((_) async {});
      when(() => mockSaveRefreshedTokens()).thenAnswer((_) async {});
      when(
        () => mockGetRefreshToken(),
      ).thenAnswer((_) async => 'refresh_token');
      when(
        () => mockGetAccessToken(),
      ).thenAnswer((_) async => 'new_access_token');

      final retryException = DioException(
        requestOptions: options,
        type: DioExceptionType.badResponse,
        response: Response<dynamic>(
          requestOptions: options,
          statusCode: 401,
        ),
      );

      when(
        () => mockDio.request<dynamic>(
          any(),
          data: any(named: 'data'),
          queryParameters: any(named: 'queryParameters'),
          options: any(named: 'options'),
          cancelToken: any(named: 'cancelToken'),
        ),
      ).thenThrow(retryException);

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockClearAuthState()).called(1);
      verify(() => mockErrorHandler.next(retryException)).called(1);
      verify(() => mockGetRefreshToken()).called(1);
    });

    test('should set retry metadata to prevent infinite loops', () async {
      final options = RequestOptions(
        path: '/api/items',
        method: 'GET',
        headers: {},
      );

      final response = Response<dynamic>(
        requestOptions: options,
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      );

      when(() => mockRefreshTokens()).thenAnswer((_) async {});
      when(() => mockSaveRefreshedTokens()).thenAnswer((_) async {});
      when(
        () => mockGetRefreshToken(),
      ).thenAnswer((_) async => 'refresh_token');
      when(
        () => mockGetAccessToken(),
      ).thenAnswer((_) async => 'new_access_token');

      final retryResponse = Response<dynamic>(
        requestOptions: options,
        statusCode: 200,
        data: {'success': true},
      );

      when(
        () => mockDio.request<dynamic>(
          any(),
          data: any(named: 'data'),
          queryParameters: any(named: 'queryParameters'),
          options: any(named: 'options'),
          cancelToken: any(named: 'cancelToken'),
        ),
      ).thenAnswer((_) async => retryResponse);

      await authInterceptor.onError(dioException, mockErrorHandler);

      // Verify that the retry metadata was set
      expect(options.extra['_auth_retry_attempted'], isTrue);
      verify(() => mockGetRefreshToken()).called(1);
      verifyNever(() => mockClearAuthState());
    });

    test('should clear auth state when refresh token is missing', () async {
      final options = RequestOptions(
        path: '/api/items',
        method: 'GET',
        headers: {},
      );

      final response = Response<dynamic>(
        requestOptions: options,
        statusCode: 401,
      );
      final dioException = DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      );

      when(() => mockGetRefreshToken()).thenAnswer((_) async => null);

      await authInterceptor.onError(dioException, mockErrorHandler);

      verify(() => mockGetRefreshToken()).called(1);
      verifyNever(() => mockRefreshTokens());
      verify(() => mockClearAuthState()).called(1);
      verify(() => mockErrorHandler.next(dioException)).called(1);
    });
  });
}
