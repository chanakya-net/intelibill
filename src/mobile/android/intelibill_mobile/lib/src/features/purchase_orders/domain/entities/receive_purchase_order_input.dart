import 'package:equatable/equatable.dart';

class ReceivePurchaseOrderInput extends Equatable {
  const ReceivePurchaseOrderInput({
    this.referenceNumber,
    this.notes,
    required this.receivedAt,
    required this.lines,
  });

  final String? referenceNumber;
  final String? notes;
  final DateTime receivedAt;
  final List<ReceivePurchaseOrderLineInput> lines;

  @override
  List<Object?> get props => [
    referenceNumber,
    notes,
    receivedAt,
    lines,
  ];
}

class ReceivePurchaseOrderLineInput extends Equatable {
  const ReceivePurchaseOrderLineInput({
    required this.purchaseOrderLineId,
    required this.barcode,
    required this.batchNumber,
    required this.quantity,
    required this.totalPurchaseCost,
    required this.mrp,
    required this.salesPrice,
    required this.taxRatePercent,
    required this.taxIncluded,
    required this.purchaseTaxIncluded,
    this.expiryDate,
    this.manufacturingDate,
  });

  final String purchaseOrderLineId;
  final String barcode;
  final String batchNumber;
  final double quantity;
  final double totalPurchaseCost;
  final double mrp;
  final double salesPrice;
  final double taxRatePercent;
  final bool taxIncluded;
  final bool purchaseTaxIncluded;
  final DateTime? expiryDate;
  final DateTime? manufacturingDate;

  @override
  List<Object?> get props => [
    purchaseOrderLineId,
    barcode,
    batchNumber,
    quantity,
    totalPurchaseCost,
    mrp,
    salesPrice,
    taxRatePercent,
    taxIncluded,
    purchaseTaxIncluded,
    expiryDate,
    manufacturingDate,
  ];
}
