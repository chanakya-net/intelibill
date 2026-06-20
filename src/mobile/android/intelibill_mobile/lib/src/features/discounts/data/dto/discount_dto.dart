import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';

part 'discount_dto.freezed.dart';
part 'discount_dto.g.dart';

@freezed
sealed class DiscountDto with _$DiscountDto {
  const factory DiscountDto({
    @JsonKey(name: 'discountId') required String discountId,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'discountType') required String discountType,
    @JsonKey(name: 'discountValue') required double discountValue,
    @JsonKey(name: 'batchPercentage') required double? batchPercentage,
    @JsonKey(name: 'isEnabled') required bool isEnabled,
    @JsonKey(name: 'createdAt') required String createdAt,
  }) = _DiscountDto;

  factory DiscountDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountDtoFromJson(json);
}

extension DiscountDtoX on DiscountDto {
  Discount toDomain() => Discount(
    discountId: discountId,
    name: name,
    discountType: DiscountType.values.firstWhere(
      (e) => e.name == discountType,
      orElse: () => DiscountType.fixed,
    ),
    discountValue: discountValue,
    batchPercentage: batchPercentage,
    isEnabled: isEnabled,
    createdAt: DateTime.parse(createdAt),
  );
}
