import 'package:intelibill_mobile/src/core/errors/failure.dart';

class AppException implements Exception {
  AppException({required this.failure, this.stackTrace});

  final Failure failure;
  final StackTrace? stackTrace;

  @override
  String toString() {
    return 'AppException: $failure';
  }
}
