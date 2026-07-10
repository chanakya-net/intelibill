// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'expenses_page_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ExpensesPageDto _$ExpensesPageDtoFromJson(Map<String, dynamic> json) =>
    _ExpensesPageDto(
      items:
          (json['items'] as List<dynamic>?)
              ?.map(
                (e) => ExpenseListItemDto.fromJson(e as Map<String, dynamic>),
              )
              .toList() ??
          const [],
      totalCount: (json['totalCount'] as num).toInt(),
      pageNumber: (json['pageNumber'] as num).toInt(),
      pageSize: (json['pageSize'] as num).toInt(),
    );

Map<String, dynamic> _$ExpensesPageDtoToJson(_ExpensesPageDto instance) =>
    <String, dynamic>{
      'items': instance.items,
      'totalCount': instance.totalCount,
      'pageNumber': instance.pageNumber,
      'pageSize': instance.pageSize,
    };
