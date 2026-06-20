// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_preview_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountPreviewRequestDto _$DiscountPreviewRequestDtoFromJson(
  Map<String, dynamic> json,
) => _DiscountPreviewRequestDto(
  name: json['name'] as String,
  discountType: json['discountType'] as String,
  discountValue: (json['discountValue'] as num).toDouble(),
  batchPercentage: (json['batchPercentage'] as num?)?.toDouble(),
);

Map<String, dynamic> _$DiscountPreviewRequestDtoToJson(
  _DiscountPreviewRequestDto instance,
) => <String, dynamic>{
  'name': instance.name,
  'discountType': instance.discountType,
  'discountValue': instance.discountValue,
  'batchPercentage': instance.batchPercentage,
};
