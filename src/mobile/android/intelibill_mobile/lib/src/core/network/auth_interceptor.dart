import 'package:dio/dio.dart';

typedef AccessTokenProvider = Future<String?> Function();
typedef RefreshTokenProvider = Future<String?> Function();
typedef TokenRefresher = Future<void> Function();
typedef TokenSaver = Future<void> Function();
typedef AuthStateClearer = Future<void> Function();

class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required this.getAccessToken,
    required this.getRefreshToken,
    required this.refreshTokens,
    required this.saveRefreshedTokens,
    required this.clearAuthState,
    required this.dio,
  });

  final AccessTokenProvider getAccessToken;
  final RefreshTokenProvider getRefreshToken;
  final TokenRefresher refreshTokens;
  final TokenSaver saveRefreshedTokens;
  final AuthStateClearer clearAuthState;
  final Dio dio;

  static const String _retryMetadataKey = '_auth_retry_attempted';
  static const String _loginEndpoint = '/auth/login';
  static const String _refreshEndpoint = '/auth/token/refresh';
  static const String _revokeEndpoint = '/auth/token/revoke';

  bool _isAuthEndpoint(String path) {
    return path.contains(_loginEndpoint) ||
        path.contains(_refreshEndpoint) ||
        path.contains(_revokeEndpoint);
  }

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await getAccessToken();

    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }

    return handler.next(options);
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    if (err.response?.statusCode != 401) {
      return handler.next(err);
    }

    if (_isAuthEndpoint(err.requestOptions.path)) {
      return handler.next(err);
    }

    final alreadyRetried =
        err.requestOptions.extra[_retryMetadataKey] as bool? ?? false;
    if (alreadyRetried) {
      await clearAuthState();
      return handler.next(err);
    }

    final refreshToken = await getRefreshToken();
    if (refreshToken == null || refreshToken.isEmpty) {
      await clearAuthState();
      return handler.next(err);
    }

    try {
      await refreshTokens();
      await saveRefreshedTokens();

      final newAccessToken = await getAccessToken();
      if (newAccessToken == null || newAccessToken.isEmpty) {
        await clearAuthState();
        return handler.next(err);
      }

      err.requestOptions.headers['Authorization'] = 'Bearer $newAccessToken';

      err.requestOptions.extra[_retryMetadataKey] = true;

      try {
        final response = await dio.request<dynamic>(
          err.requestOptions.path,
          data: err.requestOptions.data,
          queryParameters: err.requestOptions.queryParameters,
          options: Options(
            method: err.requestOptions.method,
            headers: err.requestOptions.headers,
            responseType: err.requestOptions.responseType,
            contentType: err.requestOptions.contentType,
            validateStatus: err.requestOptions.validateStatus,
            receiveDataWhenStatusError:
                err.requestOptions.receiveDataWhenStatusError,
            followRedirects: err.requestOptions.followRedirects,
            maxRedirects: err.requestOptions.maxRedirects,
            requestEncoder: err.requestOptions.requestEncoder,
            responseDecoder: err.requestOptions.responseDecoder,
            extra: err.requestOptions.extra,
          ),
          cancelToken: err.requestOptions.cancelToken,
        );
        return handler.resolve(response);
      } catch (retryError) {
        if (retryError is DioException) {
          if (retryError.response?.statusCode == 401) {
            await clearAuthState();
          }
          return handler.next(retryError);
        }
        rethrow;
      }
    } on DioException catch (_) {
      await clearAuthState();
      return handler.next(err);
    } on Exception catch (_) {
      await clearAuthState();
      return handler.next(err);
    }
  }
}
