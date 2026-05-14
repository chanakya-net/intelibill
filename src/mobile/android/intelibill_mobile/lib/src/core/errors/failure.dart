import 'package:freezed_annotation/freezed_annotation.dart';

part 'failure.freezed.dart';

@freezed
sealed class Failure with _$Failure {
  const factory Failure.validation({
    String? message,
    Map<String, List<String>>? errors,
  }) = ValidationFailure;

  const factory Failure.unauthorized({
    String? message,
  }) = UnauthorizedFailure;

  const factory Failure.forbidden({
    String? message,
  }) = ForbiddenFailure;

  const factory Failure.notFound({
    String? message,
  }) = NotFoundFailure;

  const factory Failure.server({
    String? message,
    int? statusCode,
  }) = ServerFailure;

  const factory Failure.network({
    String? message,
  }) = NetworkFailure;

  const factory Failure.timeout({
    String? message,
  }) = TimeoutFailure;

  const factory Failure.serialization({
    String? message,
  }) = SerializationFailure;

  const factory Failure.unknown({
    String? message,
  }) = UnknownFailure;
}
