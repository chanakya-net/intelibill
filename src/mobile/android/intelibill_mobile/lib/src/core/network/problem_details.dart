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
    @JsonKey(fromJson: _errorsFromJson)
    @Default({})
    Map<String, dynamic> errors,
  }) = _ProblemDetails;

  factory ProblemDetails.fromJson(Map<String, dynamic> json) =>
      _$ProblemDetailsFromJson(json);
}

Map<String, dynamic> _errorsFromJson(Object? value) {
  if (value is Map<String, dynamic>) {
    return value;
  }

  if (value is Map) {
    return value.map((key, value) => MapEntry(key.toString(), value));
  }

  if (value is List) {
    final errors = <String, List<String>>{};
    for (final item in value) {
      if (item is! Map) continue;

      final code = item['code']?.toString();
      final description = item['description']?.toString();
      if (code == null ||
          code.trim().isEmpty ||
          description == null ||
          description.trim().isEmpty) {
        continue;
      }

      errors.putIfAbsent(code, () => []).add(description);
    }
    return errors;
  }

  return {};
}
