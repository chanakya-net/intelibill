import 'dart:io';

import 'package:dio/dio.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/problem_details.dart';

class ApiErrorMapper {
  static Failure map(Object error) {
    if (error is DioException) {
      return _mapDioException(error);
    }
    return Failure.unknown(message: error.toString());
  }

  static Failure _mapDioException(DioException error) {
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return Failure.timeout(message: error.message);
      case DioExceptionType.badResponse:
        return _mapBadResponse(error.response);
      case DioExceptionType.connectionError:
        return Failure.network(
          message: error.message ?? 'Network connection error',
        );
      case DioExceptionType.cancel:
        return const Failure.unknown(message: 'Request cancelled');
      case DioExceptionType.badCertificate:
        return const Failure.unknown(message: 'Bad certificate');
      case DioExceptionType.unknown:
        if (error.error is SocketException) {
          return const Failure.network(message: 'No internet connection');
        }
        return Failure.unknown(message: error.message ?? 'Unknown error');
    }
  }

  static Failure _mapBadResponse(Response<dynamic>? response) {
    if (response == null) {
      return const Failure.server(message: 'No response from server');
    }

    final data = response.data;
    if (data is Map<String, dynamic>) {
      try {
        final problem = ProblemDetails.fromJson(data);
        return _mapProblemDetails(problem, response.statusCode);
      } on Object catch (error) {
        return Failure.serialization(
          message: 'Failed to parse ProblemDetails: $error',
        );
      }
    }

    final statusCode = response.statusCode;
    if (statusCode == 401) {
      return const Failure.unauthorized();
    } else if (statusCode == 403) {
      return const Failure.forbidden();
    } else if (statusCode == 404) {
      return const Failure.notFound();
    } else if (statusCode != null && statusCode >= 500) {
      return Failure.server(
        message: 'Server error occurred',
        statusCode: statusCode,
      );
    }

    return Failure.unknown(
      message: 'Unknown response error (${response.statusCode})',
    );
  }

  static Failure _mapProblemDetails(ProblemDetails problem, int? statusCode) {
    if (statusCode == 400) {
      // Map validation errors if present
      Map<String, List<String>>? validationErrors;
      if (problem.errors.isNotEmpty) {
        validationErrors = {};
        problem.errors.forEach((key, value) {
          if (value is List) {
            validationErrors![key] = value.map((e) => e.toString()).toList();
          } else if (value is String) {
            validationErrors![key] = [value];
          }
        });
      }

      return Failure.validation(
        message: problem.detail ?? problem.title ?? 'Validation failed',
        errors: validationErrors,
      );
    }

    if (statusCode == 401) {
      return Failure.unauthorized(message: problem.title);
    }
    if (statusCode == 403) {
      return Failure.forbidden(message: problem.title);
    }
    if (statusCode == 404) {
      return Failure.notFound(message: problem.title);
    }

    return Failure.server(
      message: problem.detail ?? problem.title ?? 'Server error',
      statusCode: statusCode,
    );
  }
}
