import 'package:freezed_annotation/freezed_annotation.dart';

part 'expense_mutation_request_dto.freezed.dart';
part 'expense_mutation_request_dto.g.dart';

String _dateOnlyToJson(DateTime value) {
  final year = value.year.toString().padLeft(4, '0');
  final month = value.month.toString().padLeft(2, '0');
  final day = value.day.toString().padLeft(2, '0');
  return '$year-$month-$day';
}

String _trim(String value) => value.trim();

String? _trimNullable(String? value) {
  final trimmed = value?.trim();
  return trimmed == null || trimmed.isEmpty ? null : trimmed;
}

@freezed
sealed class ExpenseMutationRequestDto with _$ExpenseMutationRequestDto {
  const factory ExpenseMutationRequestDto({
    @JsonKey(toJson: _trim) required String categoryName,
    required double amount,
    @JsonKey(toJson: _trim) required String paidTo,
    @JsonKey(toJson: _trimNullable) String? description,
    @JsonKey(toJson: _dateOnlyToJson) required DateTime expenseDate,
  }) = _ExpenseMutationRequestDto;

  factory ExpenseMutationRequestDto.fromJson(Map<String, dynamic> json) =>
      _$ExpenseMutationRequestDtoFromJson(json);
}
