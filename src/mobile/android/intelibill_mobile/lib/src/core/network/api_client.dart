import 'package:dio/dio.dart';
import 'package:intelibill_mobile/src/core/config/app_config.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/network/api_error_mapper.dart';
import 'package:intelibill_mobile/src/core/network/auth_interceptor.dart';

class ApiClient {
  ApiClient({
    Duration connectTimeout = const Duration(seconds: 30),
    Duration receiveTimeout = const Duration(seconds: 30),
    Duration sendTimeout = const Duration(seconds: 30),
    AuthInterceptor? authInterceptor,
  }) {
    _dio = _createDio(
      connectTimeout: connectTimeout,
      receiveTimeout: receiveTimeout,
      sendTimeout: sendTimeout,
    );

    // Register interceptors
    if (authInterceptor != null) {
      _dio.interceptors.add(authInterceptor);
    }
  }

  ApiClient.withAuthCallbacks({
    required AccessTokenProvider getAccessToken,
    required RefreshTokenProvider getRefreshToken,
    required TokenRefresher refreshTokens,
    required TokenSaver saveRefreshedTokens,
    required AuthStateClearer clearAuthState,
    Duration connectTimeout = const Duration(seconds: 30),
    Duration receiveTimeout = const Duration(seconds: 30),
    Duration sendTimeout = const Duration(seconds: 30),
  }) {
    _dio = _createDio(
      connectTimeout: connectTimeout,
      receiveTimeout: receiveTimeout,
      sendTimeout: sendTimeout,
    );
    _dio.interceptors.add(
      AuthInterceptor(
        getAccessToken: getAccessToken,
        getRefreshToken: getRefreshToken,
        refreshTokens: refreshTokens,
        saveRefreshedTokens: saveRefreshedTokens,
        clearAuthState: clearAuthState,
        dio: _dio,
      ),
    );
  }

  late final Dio _dio;

  Dio _createDio({
    required Duration connectTimeout,
    required Duration receiveTimeout,
    required Duration sendTimeout,
  }) {
    return Dio(
      BaseOptions(
        baseUrl: AppConfig.apiBaseUrl,
        headers: const {
          'Accept': 'application/json',
          'Content-Type': 'application/json',
        },
        connectTimeout: connectTimeout,
        receiveTimeout: receiveTimeout,
        sendTimeout: sendTimeout,
      ),
    );
  }

  Dio get dio => _dio;

  void addInterceptor(Interceptor interceptor) {
    _dio.interceptors.add(interceptor);
  }

  void removeInterceptor(Interceptor interceptor) {
    _dio.interceptors.remove(interceptor);
  }

  Future<Response<T>> get<T>(
    String path, {
    Map<String, dynamic>? queryParameters,
    Options? options,
    CancelToken? cancelToken,
  }) async {
    try {
      return await _dio.get<T>(
        path,
        queryParameters: queryParameters,
        options: options,
        cancelToken: cancelToken,
      );
    } catch (e) {
      throw AppException(failure: ApiErrorMapper.map(e));
    }
  }

  Future<Response<T>> post<T>(
    String path, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
    Options? options,
    CancelToken? cancelToken,
  }) async {
    try {
      return await _dio.post<T>(
        path,
        data: data,
        queryParameters: queryParameters,
        options: options,
        cancelToken: cancelToken,
      );
    } catch (e) {
      throw AppException(failure: ApiErrorMapper.map(e));
    }
  }

  Future<Response<T>> put<T>(
    String path, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
    Options? options,
    CancelToken? cancelToken,
  }) async {
    try {
      return await _dio.put<T>(
        path,
        data: data,
        queryParameters: queryParameters,
        options: options,
        cancelToken: cancelToken,
      );
    } catch (e) {
      throw AppException(failure: ApiErrorMapper.map(e));
    }
  }

  Future<Response<T>> patch<T>(
    String path, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
    Options? options,
    CancelToken? cancelToken,
  }) async {
    try {
      return await _dio.patch<T>(
        path,
        data: data,
        queryParameters: queryParameters,
        options: options,
        cancelToken: cancelToken,
      );
    } catch (e) {
      throw AppException(failure: ApiErrorMapper.map(e));
    }
  }

  Future<Response<T>> delete<T>(
    String path, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
    Options? options,
    CancelToken? cancelToken,
  }) async {
    try {
      return await _dio.delete<T>(
        path,
        data: data,
        queryParameters: queryParameters,
        options: options,
        cancelToken: cancelToken,
      );
    } catch (e) {
      throw AppException(failure: ApiErrorMapper.map(e));
    }
  }
}
