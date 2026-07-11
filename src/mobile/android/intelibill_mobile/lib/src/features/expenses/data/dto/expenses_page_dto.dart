import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_list_item_dto.dart';

part 'expenses_page_dto.freezed.dart';
part 'expenses_page_dto.g.dart';

@freezed
sealed class ExpensesPageDto with _$ExpensesPageDto {
  const factory ExpensesPageDto({
    @Default([]) List<ExpenseListItemDto> items,
    required int totalCount,
    required int pageNumber,
    required int pageSize,
  }) = _ExpensesPageDto;

  factory ExpensesPageDto.fromJson(Map<String, dynamic> json) =>
      _$ExpensesPageDtoFromJson(json);
}
