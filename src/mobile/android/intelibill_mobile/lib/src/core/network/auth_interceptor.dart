import 'package:dio/dio.dart';

abstract interface class TokenProvider {
  Future<String?> getAccessToken();
}

class AuthInterceptor extends Interceptor {
  AuthInterceptor({required this.tokenProvider});

  final TokenProvider tokenProvider;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await tokenProvider.getAccessToken();

    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }

    return handler.next(options);
  }
}
