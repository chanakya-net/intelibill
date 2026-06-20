// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_detail_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SaleDetailDto _$SaleDetailDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailDto(
  saleId: json['saleId'] as String,
  invoiceNumber: json['invoiceNumber'] as String,
  customerId: json['customerId'] as String?,
  customerName: json['customerName'] as String?,
  customerPhone: json['customerPhone'] as String?,
  paymentMethod: paymentMethodFromJson(json['paymentMethod']),
  soldAt: DateTime.parse(json['soldAt'] as String),
  items:
      (json['items'] as List<dynamic>?)
          ?.map((e) => SaleDetailItemDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  settlements:
      (json['settlements'] as List<dynamic>?)
          ?.map(
            (e) => SaleDetailSettlementDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  discounts:
      (json['discounts'] as List<dynamic>?)
          ?.map(
            (e) => SaleDetailDiscountDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  returns:
      (json['returns'] as List<dynamic>?)
          ?.map((e) => SaleDetailReturnDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  redemptions:
      (json['creditNoteRedemptions'] as List<dynamic>?)
          ?.map(
            (e) => SaleDetailRedemptionDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  warnings:
      (json['warnings'] as List<dynamic>?)?.map((e) => e as String).toList() ??
      const [],
  paidAmount: (json['paidAmount'] as num).toDouble(),
  dueAmount: (json['dueAmount'] as num).toDouble(),
  totalBeforeDiscount: (json['totalBeforeDiscount'] as num).toDouble(),
  totalDiscountAmount: (json['totalDiscountAmount'] as num).toDouble(),
  totalAmount: (json['totalAmount'] as num).toDouble(),
  totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
  creditNoteAppliedAmount:
      (json['creditNoteAppliedAmount'] as num?)?.toDouble() ?? 0.0,
  status: json['status'] as String?,
  refundAmount: (json['refundAmount'] as num?)?.toDouble() ?? 0.0,
  dueReductionAmount: (json['dueReductionAmount'] as num?)?.toDouble() ?? 0.0,
);

Map<String, dynamic> _$SaleDetailDtoToJson(_SaleDetailDto instance) =>
    <String, dynamic>{
      'saleId': instance.saleId,
      'invoiceNumber': instance.invoiceNumber,
      'customerId': instance.customerId,
      'customerName': instance.customerName,
      'customerPhone': instance.customerPhone,
      'paymentMethod': instance.paymentMethod,
      'soldAt': instance.soldAt.toIso8601String(),
      'items': instance.items,
      'settlements': instance.settlements,
      'discounts': instance.discounts,
      'returns': instance.returns,
      'creditNoteRedemptions': instance.redemptions,
      'warnings': instance.warnings,
      'paidAmount': instance.paidAmount,
      'dueAmount': instance.dueAmount,
      'totalBeforeDiscount': instance.totalBeforeDiscount,
      'totalDiscountAmount': instance.totalDiscountAmount,
      'totalAmount': instance.totalAmount,
      'totalTaxAmount': instance.totalTaxAmount,
      'creditNoteAppliedAmount': instance.creditNoteAppliedAmount,
      'status': instance.status,
      'refundAmount': instance.refundAmount,
      'dueReductionAmount': instance.dueReductionAmount,
    };

_SaleDetailItemDto _$SaleDetailItemDtoFromJson(Map<String, dynamic> json) =>
    _SaleDetailItemDto(
      itemId: json['saleItemId'] as String,
      name: json['itemName'] as String,
      quantity: (json['quantity'] as num).toDouble(),
      rate: (json['salesPrice'] as num).toDouble(),
      tax: (json['taxRatePercent'] as num).toDouble(),
      total: (json['totalAmount'] as num).toDouble(),
    );

Map<String, dynamic> _$SaleDetailItemDtoToJson(_SaleDetailItemDto instance) =>
    <String, dynamic>{
      'saleItemId': instance.itemId,
      'itemName': instance.name,
      'quantity': instance.quantity,
      'salesPrice': instance.rate,
      'taxRatePercent': instance.tax,
      'totalAmount': instance.total,
    };

_SaleDetailSettlementDto _$SaleDetailSettlementDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailSettlementDto(
  settlementId: json['settlementId'] as String,
  method: json['method'] as String,
  amount: (json['amount'] as num).toDouble(),
  settledAt: DateTime.parse(json['settledAt'] as String),
);

Map<String, dynamic> _$SaleDetailSettlementDtoToJson(
  _SaleDetailSettlementDto instance,
) => <String, dynamic>{
  'settlementId': instance.settlementId,
  'method': instance.method,
  'amount': instance.amount,
  'settledAt': instance.settledAt.toIso8601String(),
};

_SaleDetailDiscountDto _$SaleDetailDiscountDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailDiscountDto(
  discountId: json['discountId'] as String,
  type: json['type'] as String,
  value: json['value'] as String,
  amount: (json['amount'] as num).toDouble(),
);

Map<String, dynamic> _$SaleDetailDiscountDtoToJson(
  _SaleDetailDiscountDto instance,
) => <String, dynamic>{
  'discountId': instance.discountId,
  'type': instance.type,
  'value': instance.value,
  'amount': instance.amount,
};

_SaleDetailReturnDto _$SaleDetailReturnDtoFromJson(Map<String, dynamic> json) =>
    _SaleDetailReturnDto(
      returnId: json['saleReturnId'] as String,
      returnNumber: json['returnNumber'] as String,
      items:
          (json['items'] as List<dynamic>?)
              ?.map(
                (e) =>
                    SaleDetailReturnItemDto.fromJson(e as Map<String, dynamic>),
              )
              .toList() ??
          const [],
      amount: (json['totalRefundAmount'] as num).toDouble(),
      returnedAt: DateTime.parse(json['processedAt'] as String),
    );

Map<String, dynamic> _$SaleDetailReturnDtoToJson(
  _SaleDetailReturnDto instance,
) => <String, dynamic>{
  'saleReturnId': instance.returnId,
  'returnNumber': instance.returnNumber,
  'items': instance.items,
  'totalRefundAmount': instance.amount,
  'processedAt': instance.returnedAt.toIso8601String(),
};

_SaleDetailReturnItemDto _$SaleDetailReturnItemDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailReturnItemDto(
  itemId: json['saleItemId'] as String,
  itemName: json['itemName'] as String?,
  quantity: (json['quantity'] as num).toDouble(),
  amount: (json['approvedRefundAmount'] as num).toDouble(),
);

Map<String, dynamic> _$SaleDetailReturnItemDtoToJson(
  _SaleDetailReturnItemDto instance,
) => <String, dynamic>{
  'saleItemId': instance.itemId,
  'itemName': instance.itemName,
  'quantity': instance.quantity,
  'approvedRefundAmount': instance.amount,
};

_SaleDetailRedemptionDto _$SaleDetailRedemptionDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailRedemptionDto(
  redemptionId: json['creditNoteId'] as String,
  code: json['code'] as String,
  amount: (json['appliedAmount'] as num).toDouble(),
);

Map<String, dynamic> _$SaleDetailRedemptionDtoToJson(
  _SaleDetailRedemptionDto instance,
) => <String, dynamic>{
  'creditNoteId': instance.redemptionId,
  'code': instance.code,
  'appliedAmount': instance.amount,
};
