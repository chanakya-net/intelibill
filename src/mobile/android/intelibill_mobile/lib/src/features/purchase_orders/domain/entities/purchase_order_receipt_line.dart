import 'package:equatable/equatable.dart';

class PurchaseOrderReceiptLine extends Equatable {
  const PurchaseOrderReceiptLine({
    required this.receiptLineId,
    required this.purchaseOrderLineId,
    required this.itemId,
    required this.inventoryBatchId,
    this.batchNumber,
    this.batchVoided,
    required this.stockTransactionId,
    required this.quantity,
    required this.totalPurchaseCost,
    required this.unitCost,
    required this.mrp,
    required this.salesPrice,
    required this.taxRatePercent,
    required this.taxIncluded,
    required this.purchaseTaxIncluded,
  });

  final String receiptLineId;
  final String purchaseOrderLineId;
  final String itemId;
  final String inventoryBatchId;
  final String? batchNumber;
  final bool? batchVoided;
  final String stockTransactionId;
  final double quantity;
  final double totalPurchaseCost;
  final double unitCost;
  final double mrp;
  final double salesPrice;
  final double taxRatePercent;
  final bool taxIncluded;
  final bool purchaseTaxIncluded;

  @override
  List<Object?> get props => [
    receiptLineId,
    purchaseOrderLineId,
    itemId,
    inventoryBatchId,
    batchNumber,
    batchVoided,
    stockTransactionId,
    quantity,
    totalPurchaseCost,
    unitCost,
    mrp,
    salesPrice,
    taxRatePercent,
    taxIncluded,
    purchaseTaxIncluded,
  ];
}
