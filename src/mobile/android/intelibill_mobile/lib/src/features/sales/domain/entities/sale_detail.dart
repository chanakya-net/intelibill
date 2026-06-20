import 'package:equatable/equatable.dart';

class SaleDetailCreditNoteRedemption extends Equatable {
  const SaleDetailCreditNoteRedemption({
    required this.creditNoteId,
    required this.code,
    required this.appliedAmount,
  });

  final String creditNoteId;
  final String code;
  final double appliedAmount;

  double get amount => appliedAmount;

  @override
  List<Object?> get props => [creditNoteId, code, appliedAmount];
}

class SaleDetailSettlement extends Equatable {
  const SaleDetailSettlement({
    required this.settlementId,
    required this.method,
    required this.amount,
    required this.settledAt,
  });

  final String settlementId;
  final String method;
  final double amount;
  final DateTime settledAt;

  @override
  List<Object?> get props => [settlementId, method, amount, settledAt];
}

class SaleDetailDiscount extends Equatable {
  const SaleDetailDiscount({
    required this.discountId,
    required this.type,
    required this.value,
    required this.amount,
  });

  final String discountId;
  final String type;
  final String value;
  final double amount;

  @override
  List<Object?> get props => [discountId, type, value, amount];
}

class SaleDetailReturnCreditNote extends Equatable {
  const SaleDetailReturnCreditNote({
    required this.creditNoteId,
    required this.code,
    required this.originalAmount,
    required this.availableBalance,
    required this.reason,
    this.expiresAt,
  });

  final String creditNoteId;
  final String code;
  final double originalAmount;
  final double availableBalance;
  final String reason;
  final DateTime? expiresAt;

  @override
  List<Object?> get props => [
    creditNoteId,
    code,
    originalAmount,
    availableBalance,
    reason,
    expiresAt,
  ];
}

class SaleDetailReturnItem extends Equatable {
  const SaleDetailReturnItem({
    required this.saleReturnItemId,
    required this.saleItemId,
    required this.quantity,
    required this.approvedRefundAmount,
    required this.taxableAmount,
    required this.taxAmount,
    this.condition,
    this.notes,
  });

  final String saleReturnItemId;
  final String saleItemId;
  final double quantity;
  final String? condition;
  final double approvedRefundAmount;
  final double taxableAmount;
  final double taxAmount;
  final String? notes;

  String get itemId => saleItemId;
  String? get itemName => null;
  double get amount => approvedRefundAmount;

  @override
  List<Object?> get props => [
    saleReturnItemId,
    saleItemId,
    quantity,
    condition,
    approvedRefundAmount,
    taxableAmount,
    taxAmount,
    notes,
  ];
}

class SaleDetailReturn extends Equatable {
  const SaleDetailReturn({
    required this.saleReturnId,
    required this.returnNumber,
    required this.processedAt,
    required this.processedBy,
    required this.totalRefundAmount,
    required this.dueReductionAmount,
    required this.payoutAmount,
    required this.totalTaxableAmount,
    required this.totalTaxAmount,
    required this.items,
    this.notes,
    this.payoutDestination,
    this.creditNote,
  });

  final String saleReturnId;
  final String returnNumber;
  final DateTime processedAt;
  final String processedBy;
  final String? notes;
  final double totalRefundAmount;
  final double dueReductionAmount;
  final double payoutAmount;
  final String? payoutDestination;
  final double totalTaxableAmount;
  final double totalTaxAmount;
  final SaleDetailReturnCreditNote? creditNote;
  final List<SaleDetailReturnItem> items;

  double get amount => totalRefundAmount;
  DateTime get returnedAt => processedAt;

  @override
  List<Object?> get props => [
    saleReturnId,
    returnNumber,
    processedAt,
    processedBy,
    notes,
    totalRefundAmount,
    dueReductionAmount,
    payoutAmount,
    payoutDestination,
    totalTaxableAmount,
    totalTaxAmount,
    creditNote,
    items,
  ];
}

class SaleDetailItem extends Equatable {
  const SaleDetailItem({
    required this.saleItemId,
    required this.lineType,
    required this.lineCode,
    required this.itemName,
    required this.quantity,
    required this.salesPrice,
    required this.originalSalesPrice,
    required this.finalSalesPrice,
    required this.preTaxAmountBeforeDiscount,
    required this.itemDiscountAmount,
    required this.saleDiscountAmount,
    required this.taxableAmount,
    required this.taxAmount,
    required this.totalAmount,
    required this.savingsAmount,
    required this.taxRatePercent,
    required this.isPriceIncludingTax,
    required this.hasPriceMismatch,
    required this.returnedQuantity,
    required this.returnableQuantity,
    required this.returnStatus,
    this.itemId,
    this.inventoryBatchId,
    this.serviceId,
    this.hsnCode,
  });

  final String saleItemId;
  final String lineType;
  final String? itemId;
  final String? inventoryBatchId;
  final String? serviceId;
  final String lineCode;
  final String itemName;
  final double quantity;
  final double salesPrice;
  final double originalSalesPrice;
  final double finalSalesPrice;
  final double preTaxAmountBeforeDiscount;
  final double itemDiscountAmount;
  final double saleDiscountAmount;
  final double taxableAmount;
  final double taxAmount;
  final double totalAmount;
  final double savingsAmount;
  final double taxRatePercent;
  final bool isPriceIncludingTax;
  final bool hasPriceMismatch;
  final String? hsnCode;
  final double returnedQuantity;
  final double returnableQuantity;
  final String returnStatus;

  String get name => itemName;
  double get rate => salesPrice;
  double get tax => taxRatePercent;
  double get total => totalAmount;

  @override
  List<Object?> get props => [
    saleItemId,
    lineType,
    itemId,
    inventoryBatchId,
    serviceId,
    lineCode,
    itemName,
    quantity,
    salesPrice,
    originalSalesPrice,
    finalSalesPrice,
    preTaxAmountBeforeDiscount,
    itemDiscountAmount,
    saleDiscountAmount,
    taxableAmount,
    taxAmount,
    totalAmount,
    savingsAmount,
    taxRatePercent,
    isPriceIncludingTax,
    hasPriceMismatch,
    hsnCode,
    returnedQuantity,
    returnableQuantity,
    returnStatus,
  ];
}

class SaleDetail extends Equatable {
  const SaleDetail({
    required this.saleId,
    required this.invoiceNumber,
    required this.paymentMethod,
    required this.soldAt,
    required this.paidAmount,
    required this.dueAmount,
    required this.totalBeforeDiscount,
    required this.totalDiscountAmount,
    required this.totalAmount,
    required this.totalTaxAmount,
    this.creditNoteAppliedAmount = 0.0,
    this.items = const [],
    this.warnings = const [],
    this.returns = const [],
    this.creditNoteRedemptions = const [],
    this.settlements = const [],
    this.discounts = const [],
    this.status,
    this.refundAmount = 0.0,
    this.dueReductionAmount = 0.0,
    this.customerId,
    this.customerName,
    this.customerPhone,
  });

  final String saleId;
  final String invoiceNumber;
  final String? customerId;
  final String? customerName;
  final String? customerPhone;
  final int paymentMethod;
  final DateTime soldAt;
  final double paidAmount;
  final double dueAmount;
  final double totalBeforeDiscount;
  final double totalDiscountAmount;
  final double totalAmount;
  final double totalTaxAmount;
  final double creditNoteAppliedAmount;
  final List<SaleDetailItem> items;
  final List<String> warnings;
  final List<SaleDetailReturn> returns;
  final List<SaleDetailCreditNoteRedemption> creditNoteRedemptions;
  final List<SaleDetailSettlement> settlements;
  final List<SaleDetailDiscount> discounts;
  final String? status;
  final double refundAmount;
  final double dueReductionAmount;

  List<SaleDetailCreditNoteRedemption> get redemptions => creditNoteRedemptions;

  @override
  List<Object?> get props => [
    saleId,
    invoiceNumber,
    customerId,
    customerName,
    customerPhone,
    paymentMethod,
    soldAt,
    paidAmount,
    dueAmount,
    totalBeforeDiscount,
    totalDiscountAmount,
    totalAmount,
    totalTaxAmount,
    creditNoteAppliedAmount,
    items,
    warnings,
    returns,
    creditNoteRedemptions,
    settlements,
    discounts,
    status,
    refundAmount,
    dueReductionAmount,
  ];
}
