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
      (json['redemptions'] as List<dynamic>?)
          ?.map(
            (e) => SaleDetailRedemptionDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  warnings:
      (json['warnings'] as List<dynamic>?)
          ?.map((e) => SaleDetailWarningDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  paidAmount: (json['paidAmount'] as num).toDouble(),
  dueAmount: (json['dueAmount'] as num).toDouble(),
  totalBeforeDiscount: (json['totalBeforeDiscount'] as num).toDouble(),
  totalDiscountAmount: (json['totalDiscountAmount'] as num).toDouble(),
  totalAmount: (json['totalAmount'] as num).toDouble(),
  totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
  status: json['status'] as String,
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
      'redemptions': instance.redemptions,
      'warnings': instance.warnings,
      'paidAmount': instance.paidAmount,
      'dueAmount': instance.dueAmount,
      'totalBeforeDiscount': instance.totalBeforeDiscount,
      'totalDiscountAmount': instance.totalDiscountAmount,
      'totalAmount': instance.totalAmount,
      'totalTaxAmount': instance.totalTaxAmount,
      'status': instance.status,
      'refundAmount': instance.refundAmount,
      'dueReductionAmount': instance.dueReductionAmount,
    };

_SaleDetailItemDto _$SaleDetailItemDtoFromJson(Map<String, dynamic> json) =>
    _SaleDetailItemDto(
      itemId: json['itemId'] as String,
      name: json['name'] as String,
      quantity: (json['quantity'] as num).toDouble(),
      rate: (json['rate'] as num).toDouble(),
      tax: (json['tax'] as num).toDouble(),
      total: (json['total'] as num).toDouble(),
    );

Map<String, dynamic> _$SaleDetailItemDtoToJson(_SaleDetailItemDto instance) =>
    <String, dynamic>{
      'itemId': instance.itemId,
      'name': instance.name,
      'quantity': instance.quantity,
      'rate': instance.rate,
      'tax': instance.tax,
      'total': instance.total,
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
      returnId: json['returnId'] as String,
      returnNumber: json['returnNumber'] as String,
      items:
          (json['items'] as List<dynamic>?)
              ?.map(
                (e) =>
                    SaleDetailReturnItemDto.fromJson(e as Map<String, dynamic>),
              )
              .toList() ??
          const [],
      amount: (json['amount'] as num).toDouble(),
      returnedAt: DateTime.parse(json['returnedAt'] as String),
    );

Map<String, dynamic> _$SaleDetailReturnDtoToJson(
  _SaleDetailReturnDto instance,
) => <String, dynamic>{
  'returnId': instance.returnId,
  'returnNumber': instance.returnNumber,
  'items': instance.items,
  'amount': instance.amount,
  'returnedAt': instance.returnedAt.toIso8601String(),
};

_SaleDetailReturnItemDto _$SaleDetailReturnItemDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailReturnItemDto(
  itemId: json['itemId'] as String,
  itemName: json['itemName'] as String?,
  quantity: (json['quantity'] as num).toDouble(),
  amount: (json['amount'] as num).toDouble(),
);

Map<String, dynamic> _$SaleDetailReturnItemDtoToJson(
  _SaleDetailReturnItemDto instance,
) => <String, dynamic>{
  'itemId': instance.itemId,
  'itemName': instance.itemName,
  'quantity': instance.quantity,
  'amount': instance.amount,
};

_SaleDetailRedemptionDto _$SaleDetailRedemptionDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailRedemptionDto(
  redemptionId: json['redemptionId'] as String,
  type: json['type'] as String,
  amount: (json['amount'] as num).toDouble(),
  redeemedAt: DateTime.parse(json['redeemedAt'] as String),
);

Map<String, dynamic> _$SaleDetailRedemptionDtoToJson(
  _SaleDetailRedemptionDto instance,
) => <String, dynamic>{
  'redemptionId': instance.redemptionId,
  'type': instance.type,
  'amount': instance.amount,
  'redeemedAt': instance.redeemedAt.toIso8601String(),
};

_SaleDetailWarningDto _$SaleDetailWarningDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailWarningDto(
  warningId: json['warningId'] as String,
  type: json['type'] as String,
  message: json['message'] as String,
);

Map<String, dynamic> _$SaleDetailWarningDtoToJson(
  _SaleDetailWarningDto instance,
) => <String, dynamic>{
  'warningId': instance.warningId,
  'type': instance.type,
  'message': instance.message,
};
