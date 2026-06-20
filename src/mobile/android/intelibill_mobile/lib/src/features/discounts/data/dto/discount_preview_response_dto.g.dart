// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_preview_response_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountPreviewResponseDto _$DiscountPreviewResponseDtoFromJson(
  Map<String, dynamic> json,
) => _DiscountPreviewResponseDto(
  totalCostReduction: (json['totalCostReduction'] as num).toDouble(),
  error: json['error'] as String?,
  estimatedProfit: (json['estimatedProfit'] as num).toDouble(),
);

Map<String, dynamic> _$DiscountPreviewResponseDtoToJson(
  _DiscountPreviewResponseDto instance,
) => <String, dynamic>{
  'totalCostReduction': instance.totalCostReduction,
  'error': instance.error,
  'estimatedProfit': instance.estimatedProfit,
};
