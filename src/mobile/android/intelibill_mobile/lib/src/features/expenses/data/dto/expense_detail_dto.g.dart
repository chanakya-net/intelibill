// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'expense_detail_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ExpenseDetailDto _$ExpenseDetailDtoFromJson(Map<String, dynamic> json) =>
    _ExpenseDetailDto(
      id: json['id'] as String,
      shopId: json['shopId'] as String,
      categoryId: json['categoryId'] as String,
      categoryName: json['categoryName'] as String,
      amount: (json['amount'] as num).toDouble(),
      paidTo: json['paidTo'] as String,
      description: json['description'] as String?,
      expenseDate: DateTime.parse(json['expenseDate'] as String),
      actorUserId: json['actorUserId'] as String,
      isVoided: json['isVoided'] as bool,
      originalExpenseId: json['originalExpenseId'] as String?,
      supplierLedgerEntryId: json['supplierLedgerEntryId'] as String?,
      createdAt: DateTime.parse(json['createdAt'] as String),
    );

Map<String, dynamic> _$ExpenseDetailDtoToJson(_ExpenseDetailDto instance) =>
    <String, dynamic>{
      'id': instance.id,
      'shopId': instance.shopId,
      'categoryId': instance.categoryId,
      'categoryName': instance.categoryName,
      'amount': instance.amount,
      'paidTo': instance.paidTo,
      'description': instance.description,
      'expenseDate': instance.expenseDate.toIso8601String(),
      'actorUserId': instance.actorUserId,
      'isVoided': instance.isVoided,
      'originalExpenseId': instance.originalExpenseId,
      'supplierLedgerEntryId': instance.supplierLedgerEntryId,
      'createdAt': instance.createdAt.toIso8601String(),
    };
