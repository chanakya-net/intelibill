// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_list_item_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SaleListItemDto _$SaleListItemDtoFromJson(Map<String, dynamic> json) =>
    _SaleListItemDto(
      saleId: json['saleId'] as String,
      invoiceNumber: json['invoiceNumber'] as String,
      customerId: json['customerId'] as String?,
      paymentMethod: paymentMethodFromJson(json['paymentMethod']),
      soldAt: DateTime.parse(json['soldAt'] as String),
      paidAmount: (json['paidAmount'] as num).toDouble(),
      dueAmount: (json['dueAmount'] as num).toDouble(),
      totalBeforeDiscount: (json['totalBeforeDiscount'] as num).toDouble(),
      totalDiscountAmount: (json['totalDiscountAmount'] as num).toDouble(),
      totalAmount: (json['totalAmount'] as num).toDouble(),
      totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
      customerName: json['customerName'] as String?,
      customerPhone: json['customerPhone'] as String?,
      itemCount: (json['itemCount'] as num).toInt(),
      returnNumbers:
          (json['returnNumbers'] as List<dynamic>?)
              ?.map((e) => e as String)
              .toList() ??
          const [],
      status: json['status'] as String,
      refundAmount: (json['refundAmount'] as num?)?.toDouble() ?? 0.0,
      dueReductionAmount:
          (json['dueReductionAmount'] as num?)?.toDouble() ?? 0.0,
    );

Map<String, dynamic> _$SaleListItemDtoToJson(_SaleListItemDto instance) =>
    <String, dynamic>{
      'saleId': instance.saleId,
      'invoiceNumber': instance.invoiceNumber,
      'customerId': instance.customerId,
      'paymentMethod': instance.paymentMethod,
      'soldAt': instance.soldAt.toIso8601String(),
      'paidAmount': instance.paidAmount,
      'dueAmount': instance.dueAmount,
      'totalBeforeDiscount': instance.totalBeforeDiscount,
      'totalDiscountAmount': instance.totalDiscountAmount,
      'totalAmount': instance.totalAmount,
      'totalTaxAmount': instance.totalTaxAmount,
      'customerName': instance.customerName,
      'customerPhone': instance.customerPhone,
      'itemCount': instance.itemCount,
      'returnNumbers': instance.returnNumbers,
      'status': instance.status,
      'refundAmount': instance.refundAmount,
      'dueReductionAmount': instance.dueReductionAmount,
    };
