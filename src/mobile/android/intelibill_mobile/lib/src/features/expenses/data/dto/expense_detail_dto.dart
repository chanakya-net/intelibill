import 'package:freezed_annotation/freezed_annotation.dart';

part 'expense_detail_dto.freezed.dart';
part 'expense_detail_dto.g.dart';

@freezed
sealed class ExpenseDetailDto with _$ExpenseDetailDto {
  const factory ExpenseDetailDto({
    required String id,
    required String shopId,
    required String categoryId,
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
    required String actorUserId,
    required bool isVoided,
    String? originalExpenseId,
    String? supplierLedgerEntryId,
    required DateTime createdAt,
  }) = _ExpenseDetailDto;

  factory ExpenseDetailDto.fromJson(Map<String, dynamic> json) =>
      _$ExpenseDetailDtoFromJson(json);
}
