import 'package:equatable/equatable.dart';

class PurchaseOrderDraftLine extends Equatable {
  const PurchaseOrderDraftLine({
    required this.itemId,
    required this.description,
    required this.expectedQuantity,
    required this.unitCost,
  });

  final String itemId;
  final String description;
  final int expectedQuantity;
  final double unitCost;

  double get lineTotal => expectedQuantity * unitCost;

  static void validate({
    required String itemId,
    required String description,
    required int expectedQuantity,
    required double unitCost,
  }) {
    if (expectedQuantity <= 0) {
      throw ArgumentError('Quantity must be positive');
    }
    if (unitCost < 0) {
      throw ArgumentError('Unit cost must be non-negative');
    }
    if (description.length > 255) {
      throw ArgumentError('Description max 255 characters');
    }
  }

  @override
  List<Object?> get props => [itemId, description, expectedQuantity, unitCost];
}

class PurchaseOrderDraft extends Equatable {
  const PurchaseOrderDraft({
    this.supplierId,
    this.orderDate,
    this.expectedDeliveryDate,
    this.supplierReferenceNumber,
    this.notes,
    this.supplierName,
    this.supplierReference,
    this.lines = const [],
  });

  final String? supplierId;
  final DateTime? orderDate;
  final DateTime? expectedDeliveryDate;
  final String? supplierReferenceNumber;
  final String? notes;
  final String? supplierName;
  final String? supplierReference;
  final List<PurchaseOrderDraftLine> lines;

  @override
  List<Object?> get props => [
    supplierId,
    orderDate,
    expectedDeliveryDate,
    supplierReferenceNumber,
    notes,
    supplierName,
    supplierReference,
    lines,
  ];
}
