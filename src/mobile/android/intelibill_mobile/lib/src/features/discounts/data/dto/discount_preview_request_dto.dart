import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';

part 'discount_preview_request_dto.freezed.dart';
part 'discount_preview_request_dto.g.dart';

@freezed
sealed class DiscountPreviewRequestDto with _$DiscountPreviewRequestDto {
  const factory DiscountPreviewRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'discountType') required String discountType,
    @JsonKey(name: 'discountValue') required double discountValue,
    @JsonKey(name: 'batchPercentage') required double? batchPercentage,
  }) = _DiscountPreviewRequestDto;

  factory DiscountPreviewRequestDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountPreviewRequestDtoFromJson(json);

  factory DiscountPreviewRequestDto.fromDomain({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) => DiscountPreviewRequestDto(
    name: name,
    discountType: discountType.name,
    discountValue: discountValue,
    batchPercentage: batchPercentage,
  );
}
