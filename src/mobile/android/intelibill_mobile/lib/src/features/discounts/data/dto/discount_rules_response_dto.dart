import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_list_item_dto.dart';

part 'discount_rules_response_dto.freezed.dart';
part 'discount_rules_response_dto.g.dart';

@freezed
sealed class DiscountRulesResponseDto with _$DiscountRulesResponseDto {
  const factory DiscountRulesResponseDto({
    @JsonKey(name: 'items') @Default([]) List<DiscountRuleListItemDto> items,
    @JsonKey(name: 'totalCount') required int totalCount,
    @JsonKey(name: 'pageNumber') required int pageNumber,
    @JsonKey(name: 'pageSize') required int pageSize,
  }) = _DiscountRulesResponseDto;

  factory DiscountRulesResponseDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountRulesResponseDtoFromJson(json);
}
