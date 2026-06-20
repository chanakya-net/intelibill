import 'package:freezed_annotation/freezed_annotation.dart';

part 'discount_rule_dto.freezed.dart';
part 'discount_rule_dto.g.dart';

@freezed
sealed class DiscountRuleDto with _$DiscountRuleDto {
  const factory DiscountRuleDto({
    @JsonKey(name: 'id') required String id,
    @JsonKey(name: 'ruleType') required String ruleType,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,
    @JsonKey(name: 'percentage') required double percentage,
    @JsonKey(name: 'thresholdAmount') double? thresholdAmount,
    @JsonKey(name: 'startsAt') DateTime? startsAt,
    @JsonKey(name: 'endsAt') DateTime? endsAt,
    @JsonKey(name: 'isActive') required bool isActive,
    @JsonKey(name: 'disabledAt') DateTime? disabledAt,
    @JsonKey(name: 'disabledReason') String? disabledReason,
    @JsonKey(name: 'belowCostConfirmed') required bool belowCostConfirmed,
    @JsonKey(name: 'belowCostConfirmationReason')
    String? belowCostConfirmationReason,
    @JsonKey(name: 'replacesRuleId') String? replacesRuleId,
    @JsonKey(name: 'replacedByRuleId') String? replacedByRuleId,
    @JsonKey(name: 'createdAt') required DateTime createdAt,
    @JsonKey(name: 'updatedAt') DateTime? updatedAt,
  }) = _DiscountRuleDto;

  factory DiscountRuleDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountRuleDtoFromJson(json);
}
