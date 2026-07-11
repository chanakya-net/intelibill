import 'package:freezed_annotation/freezed_annotation.dart';

part 'expense_list_item_dto.freezed.dart';
part 'expense_list_item_dto.g.dart';

@freezed
sealed class ExpenseListItemDto with _$ExpenseListItemDto {
  const factory ExpenseListItemDto({
    required String id,
    required double amount,
    required String categoryName,
    required String paidTo,
    required DateTime expenseDate,
    required bool isVoided,
  }) = _ExpenseListItemDto;

  factory ExpenseListItemDto.fromJson(Map<String, dynamic> json) =>
      _$ExpenseListItemDtoFromJson(json);
}
