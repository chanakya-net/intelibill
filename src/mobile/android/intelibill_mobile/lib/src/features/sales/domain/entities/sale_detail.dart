import 'package:equatable/equatable.dart';

class SaleDetail extends Equatable {
  const SaleDetail({
    required this.saleId,
    required this.invoiceNumber,
    required this.customerId,
    required this.customerName,
    required this.customerPhone,
    required this.paymentMethod,
    required this.soldAt,
    required this.items,
    required this.settlements,
    required this.discounts,
    required this.returns,
    required this.redemptions,
    required this.warnings,
    required this.paidAmount,
    required this.dueAmount,
    required this.totalBeforeDiscount,
    required this.totalDiscountAmount,
    required this.totalAmount,
    required this.totalTaxAmount,
    required this.status,
    required this.refundAmount,
    required this.dueReductionAmount,
  });

  final String saleId;
  final String invoiceNumber;
  final String? customerId;
  final String? customerName;
  final String? customerPhone;
  final int paymentMethod;
  final DateTime soldAt;
  final List<SaleDetailItem> items;
  final List<SaleDetailSettlement> settlements;
  final List<SaleDetailDiscount> discounts;
  final List<SaleDetailReturn> returns;
  final List<SaleDetailRedemption> redemptions;
  final List<SaleDetailWarning> warnings;
  final double paidAmount;
  final double dueAmount;
  final double totalBeforeDiscount;
  final double totalDiscountAmount;
  final double totalAmount;
  final double totalTaxAmount;
  final String status;
  final double refundAmount;
  final double dueReductionAmount;

  @override
  List<Object?> get props => [
    saleId,
    invoiceNumber,
    customerId,
    customerName,
    customerPhone,
    paymentMethod,
    soldAt,
    items,
    settlements,
    discounts,
    returns,
    redemptions,
    warnings,
    paidAmount,
    dueAmount,
    totalBeforeDiscount,
    totalDiscountAmount,
    totalAmount,
    totalTaxAmount,
    status,
    refundAmount,
    dueReductionAmount,
  ];
}

class SaleDetailItem extends Equatable {
  const SaleDetailItem({
    required this.itemId,
    required this.name,
    required this.quantity,
    required this.rate,
    required this.tax,
    required this.total,
  });

  final String itemId;
  final String name;
  final double quantity;
  final double rate;
  final double tax;
  final double total;

  @override
  List<Object?> get props => [itemId, name, quantity, rate, tax, total];
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

class SaleDetailReturn extends Equatable {
  const SaleDetailReturn({
    required this.returnId,
    required this.returnNumber,
    required this.items,
    required this.amount,
    required this.returnedAt,
  });

  final String returnId;
  final String returnNumber;
  final List<SaleDetailReturnItem> items;
  final double amount;
  final DateTime returnedAt;

  @override
  List<Object?> get props => [returnId, returnNumber, items, amount, returnedAt];
}

class SaleDetailReturnItem extends Equatable {
  const SaleDetailReturnItem({
    required this.itemId,
    this.itemName,
    required this.quantity,
    required this.amount,
  });

  final String itemId;
  final String? itemName;
  final double quantity;
  final double amount;

  @override
  List<Object?> get props => [itemId, itemName, quantity, amount];
}

class SaleDetailRedemption extends Equatable {
  const SaleDetailRedemption({
    required this.redemptionId,
    required this.type,
    required this.amount,
    required this.redeemedAt,
  });

  final String redemptionId;
  final String type;
  final double amount;
  final DateTime redeemedAt;

  @override
  List<Object?> get props => [redemptionId, type, amount, redeemedAt];
}

class SaleDetailWarning extends Equatable {
  const SaleDetailWarning({
    required this.warningId,
    required this.type,
    required this.message,
  });

  final String warningId;
  final String type;
  final String message;

  @override
  List<Object?> get props => [warningId, type, message];
}
