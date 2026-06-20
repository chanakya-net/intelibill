// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_detail_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SaleDetailCreditNoteRedemptionDto _$SaleDetailCreditNoteRedemptionDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailCreditNoteRedemptionDto(
  creditNoteId: json['creditNoteId'] as String,
  code: json['code'] as String,
  appliedAmount: (json['appliedAmount'] as num).toDouble(),
);

Map<String, dynamic> _$SaleDetailCreditNoteRedemptionDtoToJson(
  _SaleDetailCreditNoteRedemptionDto instance,
) => <String, dynamic>{
  'creditNoteId': instance.creditNoteId,
  'code': instance.code,
  'appliedAmount': instance.appliedAmount,
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

_SaleDetailReturnCreditNoteDto _$SaleDetailReturnCreditNoteDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailReturnCreditNoteDto(
  creditNoteId: json['creditNoteId'] as String,
  code: json['code'] as String,
  originalAmount: (json['originalAmount'] as num).toDouble(),
  availableBalance: (json['availableBalance'] as num).toDouble(),
  expiresAt: json['expiresAt'] == null
      ? null
      : DateTime.parse(json['expiresAt'] as String),
  reason: json['reason'] as String,
);

Map<String, dynamic> _$SaleDetailReturnCreditNoteDtoToJson(
  _SaleDetailReturnCreditNoteDto instance,
) => <String, dynamic>{
  'creditNoteId': instance.creditNoteId,
  'code': instance.code,
  'originalAmount': instance.originalAmount,
  'availableBalance': instance.availableBalance,
  'expiresAt': instance.expiresAt?.toIso8601String(),
  'reason': instance.reason,
};

_SaleDetailReturnItemDto _$SaleDetailReturnItemDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailReturnItemDto(
  saleReturnItemId: json['saleReturnItemId'] as String,
  saleItemId: json['saleItemId'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  condition: json['condition'] as String?,
  approvedRefundAmount: (json['approvedRefundAmount'] as num).toDouble(),
  taxableAmount: (json['taxableAmount'] as num).toDouble(),
  taxAmount: (json['taxAmount'] as num).toDouble(),
  notes: json['notes'] as String?,
);

Map<String, dynamic> _$SaleDetailReturnItemDtoToJson(
  _SaleDetailReturnItemDto instance,
) => <String, dynamic>{
  'saleReturnItemId': instance.saleReturnItemId,
  'saleItemId': instance.saleItemId,
  'quantity': instance.quantity,
  'condition': instance.condition,
  'approvedRefundAmount': instance.approvedRefundAmount,
  'taxableAmount': instance.taxableAmount,
  'taxAmount': instance.taxAmount,
  'notes': instance.notes,
};

_SaleDetailReturnDto _$SaleDetailReturnDtoFromJson(Map<String, dynamic> json) =>
    _SaleDetailReturnDto(
      saleReturnId: json['saleReturnId'] as String,
      returnNumber: json['returnNumber'] as String,
      processedAt: DateTime.parse(json['processedAt'] as String),
      processedBy: json['processedBy'] as String,
      notes: json['notes'] as String?,
      totalRefundAmount: (json['totalRefundAmount'] as num).toDouble(),
      dueReductionAmount: (json['dueReductionAmount'] as num).toDouble(),
      payoutAmount: (json['payoutAmount'] as num).toDouble(),
      payoutDestination: json['payoutDestination'] as String?,
      totalTaxableAmount: (json['totalTaxableAmount'] as num).toDouble(),
      totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
      creditNote: json['creditNote'] == null
          ? null
          : SaleDetailReturnCreditNoteDto.fromJson(
              json['creditNote'] as Map<String, dynamic>,
            ),
      items:
          (json['items'] as List<dynamic>?)
              ?.map(
                (e) =>
                    SaleDetailReturnItemDto.fromJson(e as Map<String, dynamic>),
              )
              .toList() ??
          const [],
    );

Map<String, dynamic> _$SaleDetailReturnDtoToJson(
  _SaleDetailReturnDto instance,
) => <String, dynamic>{
  'saleReturnId': instance.saleReturnId,
  'returnNumber': instance.returnNumber,
  'processedAt': instance.processedAt.toIso8601String(),
  'processedBy': instance.processedBy,
  'notes': instance.notes,
  'totalRefundAmount': instance.totalRefundAmount,
  'dueReductionAmount': instance.dueReductionAmount,
  'payoutAmount': instance.payoutAmount,
  'payoutDestination': instance.payoutDestination,
  'totalTaxableAmount': instance.totalTaxableAmount,
  'totalTaxAmount': instance.totalTaxAmount,
  'creditNote': instance.creditNote,
  'items': instance.items,
};

_SaleDetailItemDto _$SaleDetailItemDtoFromJson(
  Map<String, dynamic> json,
) => _SaleDetailItemDto(
  saleItemId: json['saleItemId'] as String,
  lineType: json['lineType'] as String,
  itemId: json['itemId'] as String?,
  inventoryBatchId: json['inventoryBatchId'] as String?,
  serviceId: json['serviceId'] as String?,
  lineCode: json['lineCode'] as String,
  itemName: json['itemName'] as String,
  quantity: (json['quantity'] as num).toDouble(),
  salesPrice: (json['salesPrice'] as num).toDouble(),
  originalSalesPrice: (json['originalSalesPrice'] as num?)?.toDouble() ?? 0.0,
  finalSalesPrice: (json['finalSalesPrice'] as num?)?.toDouble() ?? 0.0,
  preTaxAmountBeforeDiscount:
      (json['preTaxAmountBeforeDiscount'] as num?)?.toDouble() ?? 0.0,
  itemDiscountAmount: (json['itemDiscountAmount'] as num?)?.toDouble() ?? 0.0,
  saleDiscountAmount: (json['saleDiscountAmount'] as num?)?.toDouble() ?? 0.0,
  taxableAmount: (json['taxableAmount'] as num?)?.toDouble() ?? 0.0,
  taxAmount: (json['taxAmount'] as num?)?.toDouble() ?? 0.0,
  totalAmount: (json['totalAmount'] as num?)?.toDouble() ?? 0.0,
  savingsAmount: (json['savingsAmount'] as num?)?.toDouble() ?? 0.0,
  taxRatePercent: (json['taxRatePercent'] as num).toDouble(),
  isPriceIncludingTax: json['isPriceIncludingTax'] as bool,
  hasPriceMismatch: json['hasPriceMismatch'] as bool? ?? false,
  hsnCode: json['hsnCode'] as String?,
  returnedQuantity: (json['returnedQuantity'] as num?)?.toDouble() ?? 0.0,
  returnableQuantity: (json['returnableQuantity'] as num?)?.toDouble() ?? 0.0,
  returnStatus: json['returnStatus'] as String? ?? 'NotReturned',
);

Map<String, dynamic> _$SaleDetailItemDtoToJson(_SaleDetailItemDto instance) =>
    <String, dynamic>{
      'saleItemId': instance.saleItemId,
      'lineType': instance.lineType,
      'itemId': instance.itemId,
      'inventoryBatchId': instance.inventoryBatchId,
      'serviceId': instance.serviceId,
      'lineCode': instance.lineCode,
      'itemName': instance.itemName,
      'quantity': instance.quantity,
      'salesPrice': instance.salesPrice,
      'originalSalesPrice': instance.originalSalesPrice,
      'finalSalesPrice': instance.finalSalesPrice,
      'preTaxAmountBeforeDiscount': instance.preTaxAmountBeforeDiscount,
      'itemDiscountAmount': instance.itemDiscountAmount,
      'saleDiscountAmount': instance.saleDiscountAmount,
      'taxableAmount': instance.taxableAmount,
      'taxAmount': instance.taxAmount,
      'totalAmount': instance.totalAmount,
      'savingsAmount': instance.savingsAmount,
      'taxRatePercent': instance.taxRatePercent,
      'isPriceIncludingTax': instance.isPriceIncludingTax,
      'hasPriceMismatch': instance.hasPriceMismatch,
      'hsnCode': instance.hsnCode,
      'returnedQuantity': instance.returnedQuantity,
      'returnableQuantity': instance.returnableQuantity,
      'returnStatus': instance.returnStatus,
    };

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
  paidAmount: (json['paidAmount'] as num).toDouble(),
  dueAmount: (json['dueAmount'] as num).toDouble(),
  totalBeforeDiscount: (json['totalBeforeDiscount'] as num).toDouble(),
  totalDiscountAmount: (json['totalDiscountAmount'] as num).toDouble(),
  totalAmount: (json['totalAmount'] as num).toDouble(),
  totalTaxAmount: (json['totalTaxAmount'] as num).toDouble(),
  creditNoteAppliedAmount:
      (json['creditNoteAppliedAmount'] as num?)?.toDouble() ?? 0.0,
  items:
      (json['items'] as List<dynamic>?)
          ?.map((e) => SaleDetailItemDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  warnings:
      (json['warnings'] as List<dynamic>?)?.map((e) => e as String).toList() ??
      const [],
  returns:
      (json['returns'] as List<dynamic>?)
          ?.map((e) => SaleDetailReturnDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  creditNoteRedemptions:
      (json['creditNoteRedemptions'] as List<dynamic>?)
          ?.map(
            (e) => SaleDetailCreditNoteRedemptionDto.fromJson(
              e as Map<String, dynamic>,
            ),
          )
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
      'paidAmount': instance.paidAmount,
      'dueAmount': instance.dueAmount,
      'totalBeforeDiscount': instance.totalBeforeDiscount,
      'totalDiscountAmount': instance.totalDiscountAmount,
      'totalAmount': instance.totalAmount,
      'totalTaxAmount': instance.totalTaxAmount,
      'creditNoteAppliedAmount': instance.creditNoteAppliedAmount,
      'items': instance.items,
      'warnings': instance.warnings,
      'returns': instance.returns,
      'creditNoteRedemptions': instance.creditNoteRedemptions,
      'settlements': instance.settlements,
      'discounts': instance.discounts,
      'status': instance.status,
      'refundAmount': instance.refundAmount,
      'dueReductionAmount': instance.dueReductionAmount,
    };
