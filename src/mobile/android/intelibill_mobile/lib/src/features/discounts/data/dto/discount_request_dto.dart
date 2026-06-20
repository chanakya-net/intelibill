import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';

part 'discount_request_dto.freezed.dart';
part 'discount_request_dto.g.dart';

@freezed
sealed class DiscountRequestDto with _$DiscountRequestDto {
  const factory DiscountRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'discountType') required String discountType,
    @JsonKey(name: 'discountValue') required double discountValue,
    @JsonKey(name: 'batchPercentage') required double? batchPercentage,
  }) = _DiscountRequestDto;

  factory DiscountRequestDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountRequestDtoFromJson(json);

  factory DiscountRequestDto.fromDomain({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) => DiscountRequestDto(
    name: name,
    discountType: discountType.name,
    discountValue: discountValue,
    batchPercentage: batchPercentage,
  );
}
