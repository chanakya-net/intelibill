// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountDto _$DiscountDtoFromJson(Map<String, dynamic> json) => _DiscountDto(
  discountId: json['discountId'] as String,
  name: json['name'] as String,
  discountType: json['discountType'] as String,
  discountValue: (json['discountValue'] as num).toDouble(),
  batchPercentage: (json['batchPercentage'] as num?)?.toDouble(),
  isEnabled: json['isEnabled'] as bool,
  createdAt: json['createdAt'] as String,
);

Map<String, dynamic> _$DiscountDtoToJson(_DiscountDto instance) =>
    <String, dynamic>{
      'discountId': instance.discountId,
      'name': instance.name,
      'discountType': instance.discountType,
      'discountValue': instance.discountValue,
      'batchPercentage': instance.batchPercentage,
      'isEnabled': instance.isEnabled,
      'createdAt': instance.createdAt,
    };
