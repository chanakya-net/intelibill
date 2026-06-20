// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_rules_response_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountRulesResponseDto _$DiscountRulesResponseDtoFromJson(
  Map<String, dynamic> json,
) => _DiscountRulesResponseDto(
  items:
      (json['items'] as List<dynamic>?)
          ?.map(
            (e) => DiscountRuleListItemDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  totalCount: (json['totalCount'] as num).toInt(),
  pageNumber: (json['pageNumber'] as num).toInt(),
  pageSize: (json['pageSize'] as num).toInt(),
  pageCount: (json['pageCount'] as num?)?.toInt(),
);

Map<String, dynamic> _$DiscountRulesResponseDtoToJson(
  _DiscountRulesResponseDto instance,
) => <String, dynamic>{
  'items': instance.items,
  'totalCount': instance.totalCount,
  'pageNumber': instance.pageNumber,
  'pageSize': instance.pageSize,
  'pageCount': instance.pageCount,
};
