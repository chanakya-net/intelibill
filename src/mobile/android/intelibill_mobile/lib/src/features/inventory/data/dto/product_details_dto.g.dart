// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'product_details_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ProductDetailsDto _$ProductDetailsDtoFromJson(Map<String, dynamic> json) =>
    _ProductDetailsDto(
      name: json['name'] as String,
      description: json['description'] as String,
      uom: json['uom'] as String,
      costPrice: (json['costPrice'] as num).toDouble(),
      mrp: (json['mrp'] as num).toDouble(),
      salesPrice: (json['salesPrice'] as num).toDouble(),
      supplierId: json['supplierId'] as String?,
      supplierName: json['supplierName'] as String?,
      taxIncluded: json['taxIncluded'] as bool?,
      taxRatePercent: (json['taxRatePercent'] as num?)?.toDouble(),
    );

Map<String, dynamic> _$ProductDetailsDtoToJson(_ProductDetailsDto instance) =>
    <String, dynamic>{
      'name': instance.name,
      'description': instance.description,
      'uom': instance.uom,
      'costPrice': instance.costPrice,
      'mrp': instance.mrp,
      'salesPrice': instance.salesPrice,
      'supplierId': instance.supplierId,
      'supplierName': instance.supplierName,
      'taxIncluded': instance.taxIncluded,
      'taxRatePercent': instance.taxRatePercent,
    };
