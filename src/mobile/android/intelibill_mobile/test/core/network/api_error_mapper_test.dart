import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_error_mapper.dart';

void main() {
  group('ApiErrorMapper', () {
    test('should map connection timeout to Failure.timeout', () {
      final dioException = DioException(
        requestOptions: RequestOptions(),
        type: DioExceptionType.connectionTimeout,
        message: 'Connection timeout',
      );

      final result = ApiErrorMapper.map(dioException);

      expect(result, isA<TimeoutFailure>());
      expect((result as TimeoutFailure).message, 'Connection timeout');
    });

    test('should map 401 response to Failure.unauthorized', () {
      final dioException = DioException(
        requestOptions: RequestOptions(),
        type: DioExceptionType.badResponse,
        response: Response<dynamic>(
          requestOptions: RequestOptions(),
          statusCode: 401,
        ),
      );

      final result = ApiErrorMapper.map(dioException);

      expect(result, isA<UnauthorizedFailure>());
    });

    test('should map 400 with ProblemDetails to Failure.validation', () {
      final dioException = DioException(
        requestOptions: RequestOptions(),
        type: DioExceptionType.badResponse,
        response: Response<dynamic>(
          requestOptions: RequestOptions(),
          statusCode: 400,
          data: {
            'title': 'Validation Error',
            'errors': {
              'email': ['Invalid email'],
            },
          },
        ),
      );

      final result = ApiErrorMapper.map(dioException);

      expect(result, isA<ValidationFailure>());
      final failure = result as ValidationFailure;
      expect(failure.message, 'Validation Error');
      expect(failure.errors?['email'], contains('Invalid email'));
    });

    test('should map SocketException to Failure.network', () {
      final dioException = DioException(
        requestOptions: RequestOptions(),
        error: const SocketException('No internet'),
      );

      final result = ApiErrorMapper.map(dioException);

      expect(result, isA<NetworkFailure>());
      expect((result as NetworkFailure).message, 'No internet connection');
    });

    test('should map other exceptions to Failure.unknown', () {
      const error = 'Some random error';

      final result = ApiErrorMapper.map(error);

      expect(result, isA<UnknownFailure>());
      expect((result as UnknownFailure).message, error);
    });
  });
}
