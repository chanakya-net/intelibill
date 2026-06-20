import 'package:freezed_annotation/freezed_annotation.dart';

part 'discount_rule_list_item_dto.freezed.dart';
part 'discount_rule_list_item_dto.g.dart';

@freezed
sealed class DiscountRuleListItemDto with _$DiscountRuleListItemDto {
  const factory DiscountRuleListItemDto({
    @JsonKey(name: 'id') required String id,
    @JsonKey(name: 'ruleType') required String ruleType,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'isActive') required bool isActive,
    @JsonKey(name: 'startsAt') DateTime? startsAt,
    @JsonKey(name: 'endsAt') DateTime? endsAt,
    @JsonKey(name: 'createdAt') required DateTime createdAt,
  }) = _DiscountRuleListItemDto;

  factory DiscountRuleListItemDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountRuleListItemDtoFromJson(json);
}
