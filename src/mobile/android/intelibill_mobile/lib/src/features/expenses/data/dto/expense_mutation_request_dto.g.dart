// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'expense_mutation_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ExpenseMutationRequestDto _$ExpenseMutationRequestDtoFromJson(
  Map<String, dynamic> json,
) => _ExpenseMutationRequestDto(
  categoryName: json['categoryName'] as String,
  amount: (json['amount'] as num).toDouble(),
  paidTo: json['paidTo'] as String,
  description: json['description'] as String?,
  expenseDate: DateTime.parse(json['expenseDate'] as String),
);

Map<String, dynamic> _$ExpenseMutationRequestDtoToJson(
  _ExpenseMutationRequestDto instance,
) => <String, dynamic>{
  'categoryName': _trim(instance.categoryName),
  'amount': instance.amount,
  'paidTo': _trim(instance.paidTo),
  'description': _trimNullable(instance.description),
  'expenseDate': _dateOnlyToJson(instance.expenseDate),
};
