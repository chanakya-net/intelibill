// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_rule_list_item_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountRuleListItemDto _$DiscountRuleListItemDtoFromJson(
  Map<String, dynamic> json,
) => _DiscountRuleListItemDto(
  id: json['id'] as String,
  ruleType: json['ruleType'] as String,
  name: json['name'] as String,
  isActive: json['isActive'] as bool,
  startsAt: json['startsAt'] == null
      ? null
      : DateTime.parse(json['startsAt'] as String),
  endsAt: json['endsAt'] == null
      ? null
      : DateTime.parse(json['endsAt'] as String),
  createdAt: DateTime.parse(json['createdAt'] as String),
);

Map<String, dynamic> _$DiscountRuleListItemDtoToJson(
  _DiscountRuleListItemDto instance,
) => <String, dynamic>{
  'id': instance.id,
  'ruleType': instance.ruleType,
  'name': instance.name,
  'isActive': instance.isActive,
  'startsAt': instance.startsAt?.toIso8601String(),
  'endsAt': instance.endsAt?.toIso8601String(),
  'createdAt': instance.createdAt.toIso8601String(),
};
