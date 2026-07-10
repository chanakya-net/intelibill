// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'expense_list_item_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ExpenseListItemDto _$ExpenseListItemDtoFromJson(Map<String, dynamic> json) =>
    _ExpenseListItemDto(
      id: json['id'] as String,
      amount: (json['amount'] as num).toDouble(),
      categoryName: json['categoryName'] as String,
      paidTo: json['paidTo'] as String,
      expenseDate: DateTime.parse(json['expenseDate'] as String),
      isVoided: json['isVoided'] as bool,
    );

Map<String, dynamic> _$ExpenseListItemDtoToJson(_ExpenseListItemDto instance) =>
    <String, dynamic>{
      'id': instance.id,
      'amount': instance.amount,
      'categoryName': instance.categoryName,
      'paidTo': instance.paidTo,
      'expenseDate': instance.expenseDate.toIso8601String(),
      'isVoided': instance.isVoided,
    };
