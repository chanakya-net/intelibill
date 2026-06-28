// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_DiscountRequestDto _$DiscountRequestDtoFromJson(Map<String, dynamic> json) =>
    _DiscountRequestDto(
      name: json['name'] as String,
      discountType: json['discountType'] as String,
      discountValue: (json['discountValue'] as num).toDouble(),
      batchPercentage: (json['batchPercentage'] as num?)?.toDouble(),
    );

Map<String, dynamic> _$DiscountRequestDtoToJson(_DiscountRequestDto instance) =>
    <String, dynamic>{
      'name': instance.name,
      'discountType': instance.discountType,
      'discountValue': instance.discountValue,
      'batchPercentage': instance.batchPercentage,
    };
