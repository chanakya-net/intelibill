import 'package:freezed_annotation/freezed_annotation.dart';

part 'problem_details.freezed.dart';
part 'problem_details.g.dart';

@freezed
abstract class ProblemDetails with _$ProblemDetails {
  const factory ProblemDetails({
    String? type,
    String? title,
    int? status,
    String? detail,
    String? instance,
    @Default({}) Map<String, dynamic> errors,
  }) = _ProblemDetails;

  factory ProblemDetails.fromJson(Map<String, dynamic> json) =>
      _$ProblemDetailsFromJson(json);
}
