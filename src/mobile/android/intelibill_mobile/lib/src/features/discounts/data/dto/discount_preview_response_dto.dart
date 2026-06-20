import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';

part 'discount_preview_response_dto.freezed.dart';
part 'discount_preview_response_dto.g.dart';

@freezed
sealed class DiscountPreviewResponseDto with _$DiscountPreviewResponseDto {
  const DiscountPreviewResponseDto._();

  const factory DiscountPreviewResponseDto({
    @JsonKey(name: 'totalCostReduction') required double totalCostReduction,
    @JsonKey(name: 'error') required String? error,
    @JsonKey(name: 'estimatedProfit') required double estimatedProfit,
  }) = _DiscountPreviewResponseDto;

  factory DiscountPreviewResponseDto.fromJson(Map<String, dynamic> json) =>
      _$DiscountPreviewResponseDtoFromJson(json);

  DiscountPreview toDomain() => DiscountPreview(
    totalCostReduction: totalCostReduction,
    error: error,
    estimatedProfit: estimatedProfit,
  );
}
