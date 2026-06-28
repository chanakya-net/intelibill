import 'package:equatable/equatable.dart';

class RecordSaleLineDiscountRequest extends Equatable {
  const RecordSaleLineDiscountRequest({
    required this.type,
    required this.value,
  });

  final int type;
  final double value;

  Map<String, dynamic> toJson() {
    return {'type': type, 'value': value};
  }

  @override
  List<Object?> get props => [type, value];
}

class RecordSaleLineRequest extends Equatable {
  const RecordSaleLineRequest({
    required this.barcode,
    required this.batchNumber,
    required this.itemName,
    required this.quantity,
    required this.costPrice,
    required this.salesPrice,
    required this.mrp,
    required this.taxRatePercent,
    required this.isPriceIncludingTax,
    required this.inventoryBatchId,
    required this.clientLineKey,
    required this.lineType,
    required this.itemDiscount,
    this.hsnCode,
    this.serviceId,
  });

  final String barcode;
  final String batchNumber;
  final String itemName;
  final double quantity;
  final double costPrice;
  final double salesPrice;
  final double mrp;
  final double taxRatePercent;
  final bool isPriceIncludingTax;
  final String inventoryBatchId;
  final String clientLineKey;
  final String lineType;
  final RecordSaleLineDiscountRequest? itemDiscount;
  final String? hsnCode;
  final String? serviceId;

  Map<String, dynamic> toRequestMap() {
    return {
      'barcode': barcode,
      'batchNumber': batchNumber,
      'itemName': itemName,
      'quantity': quantity,
      'costPrice': costPrice,
      'salesPrice': salesPrice,
      'mrp': mrp,
      'taxRatePercent': taxRatePercent,
      'isPriceIncludingTax': isPriceIncludingTax,
      'inventoryBatchId': inventoryBatchId,
      'clientLineKey': clientLineKey,
      'itemDiscount': itemDiscount?.toJson(),
      'hsnCode': hsnCode,
      'lineType': lineType,
      if (serviceId != null) 'serviceId': serviceId,
    };
  }

  @override
  List<Object?> get props => [
    barcode,
    batchNumber,
    itemName,
    quantity,
    costPrice,
    salesPrice,
    mrp,
    taxRatePercent,
    isPriceIncludingTax,
    inventoryBatchId,
    clientLineKey,
    lineType,
    itemDiscount,
    hsnCode,
    serviceId,
  ];
}

class RecordSaleRequest extends Equatable {
  const RecordSaleRequest({
    required this.idempotencyKey,
    required this.customerId,
    required this.customerName,
    required this.customerPhone,
    required this.paymentMethod,
    required this.paidAmount,
    required this.dueAmount,
    required this.items,
    required this.saleDiscount,
    this.creditNoteAppliedAmount,
    this.creditNoteRedemptions = const [],
    this.creditNoteCustomerMismatchConfirmed,
  });

  final String idempotencyKey;
  final String? customerId;
  final String? customerName;
  final String? customerPhone;
  final int paymentMethod;
  final double paidAmount;
  final double dueAmount;
  final List<RecordSaleLineRequest> items;
  final RecordSaleLineDiscountRequest? saleDiscount;
  final double? creditNoteAppliedAmount;
  final List<RecordSaleCreditNoteRedemptionRequest> creditNoteRedemptions;
  final bool? creditNoteCustomerMismatchConfirmed;

  Map<String, dynamic> toRequestMap() {
    return {
      'idempotencyKey': idempotencyKey,
      if (customerId != null) 'customerId': customerId,
      if (customerName != null) 'customerName': customerName,
      if (customerPhone != null) 'customerPhone': customerPhone,
      'paymentMethod': paymentMethod,
      'paidAmount': paidAmount,
      'dueAmount': dueAmount,
      'items': items.map((item) => item.toRequestMap()).toList(growable: false),
      if (saleDiscount != null) 'saleDiscount': saleDiscount!.toJson(),
      if (creditNoteAppliedAmount != null)
        'creditNoteAppliedAmount': creditNoteAppliedAmount,
      if (creditNoteRedemptions.isNotEmpty)
        'creditNoteRedemptions': creditNoteRedemptions
            .map((item) => item.toJson())
            .toList(growable: false),
      if (creditNoteCustomerMismatchConfirmed != null)
        'creditNoteCustomerMismatchConfirmed':
            creditNoteCustomerMismatchConfirmed,
    };
  }

  @override
  List<Object?> get props => [
    idempotencyKey,
    customerId,
    customerName,
    customerPhone,
    paymentMethod,
    paidAmount,
    dueAmount,
    items,
    saleDiscount,
    creditNoteAppliedAmount,
    creditNoteRedemptions,
    creditNoteCustomerMismatchConfirmed,
  ];
}

class RecordSaleCreditNoteRedemptionRequest extends Equatable {
  const RecordSaleCreditNoteRedemptionRequest({
    required this.creditNoteId,
    required this.code,
    required this.amount,
  });

  final String creditNoteId;
  final String code;
  final double amount;

  Map<String, dynamic> toJson() {
    return {
      'creditNoteId': creditNoteId,
      'code': code,
      'amount': amount,
    };
  }

  @override
  List<Object?> get props => [creditNoteId, code, amount];
}
