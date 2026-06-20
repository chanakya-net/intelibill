// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_rule_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountRuleDto _$DiscountRuleDtoFromJson(Map<String, dynamic> json) =>
    _DiscountRuleDto(
      id: json['id'] as String,
      ruleType: json['ruleType'] as String,
      name: json['name'] as String,
      description: json['description'] as String?,
      inventoryBatchId: json['inventoryBatchId'] as String?,
      percentage: (json['percentage'] as num).toDouble(),
      thresholdAmount: (json['thresholdAmount'] as num?)?.toDouble(),
      startsAt: json['startsAt'] == null
          ? null
          : DateTime.parse(json['startsAt'] as String),
      endsAt: json['endsAt'] == null
          ? null
          : DateTime.parse(json['endsAt'] as String),
      isActive: json['isActive'] as bool,
      disabledAt: json['disabledAt'] == null
          ? null
          : DateTime.parse(json['disabledAt'] as String),
      disabledReason: json['disabledReason'] as String?,
      belowCostConfirmed: json['belowCostConfirmed'] as bool,
      belowCostConfirmationReason:
          json['belowCostConfirmationReason'] as String?,
      replacesRuleId: json['replacesRuleId'] as String?,
      replacedByRuleId: json['replacedByRuleId'] as String?,
      createdAt: DateTime.parse(json['createdAt'] as String),
      updatedAt: json['updatedAt'] == null
          ? null
          : DateTime.parse(json['updatedAt'] as String),
    );

Map<String, dynamic> _$DiscountRuleDtoToJson(_DiscountRuleDto instance) =>
    <String, dynamic>{
      'id': instance.id,
      'ruleType': instance.ruleType,
      'name': instance.name,
      'description': instance.description,
      'inventoryBatchId': instance.inventoryBatchId,
      'percentage': instance.percentage,
      'thresholdAmount': instance.thresholdAmount,
      'startsAt': instance.startsAt?.toIso8601String(),
      'endsAt': instance.endsAt?.toIso8601String(),
      'isActive': instance.isActive,
      'disabledAt': instance.disabledAt?.toIso8601String(),
      'disabledReason': instance.disabledReason,
      'belowCostConfirmed': instance.belowCostConfirmed,
      'belowCostConfirmationReason': instance.belowCostConfirmationReason,
      'replacesRuleId': instance.replacesRuleId,
      'replacedByRuleId': instance.replacedByRuleId,
      'createdAt': instance.createdAt.toIso8601String(),
      'updatedAt': instance.updatedAt?.toIso8601String(),
    };
