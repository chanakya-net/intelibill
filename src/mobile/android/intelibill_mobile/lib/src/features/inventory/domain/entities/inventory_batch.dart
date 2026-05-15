import 'package:equatable/equatable.dart';

class InventoryBatch extends Equatable {
  const InventoryBatch({
    required this.batchId,
    required this.itemId,
    required this.itemName,
    required this.itemBarcode,
    required this.itemUom,
    required this.batchNumber,
    required this.quantity,
    required this.costPrice,
    required this.mrp,
    required this.salesPrice,
    required this.taxRate,
    required this.taxIncluded,
    this.expiryDate,
    this.manufacturingDate,
    this.referenceNumber,
    this.notes,
    this.supplierId,
    this.supplierName,
    required this.isVoided,
    required this.createdAt,
  });

  final String batchId;
  final String itemId;
  final String itemName;
  final String itemBarcode;
  final String itemUom;
  final String batchNumber;
  final double quantity;
  final double costPrice;
  final double mrp;
  final double salesPrice;
  final double taxRate;
  final bool taxIncluded;
  final DateTime? expiryDate;
  final DateTime? manufacturingDate;
  final String? referenceNumber;
  final String? notes;
  final String? supplierId;
  final String? supplierName;
  final bool isVoided;
  final DateTime createdAt;

  @override
  List<Object?> get props => [
    batchId,
    itemId,
    itemName,
    itemBarcode,
    itemUom,
    batchNumber,
    quantity,
    costPrice,
    mrp,
    salesPrice,
    taxRate,
    taxIncluded,
    expiryDate,
    manufacturingDate,
    referenceNumber,
    notes,
    supplierId,
    supplierName,
    isVoided,
    createdAt,
  ];
}
