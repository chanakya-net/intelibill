import 'package:equatable/equatable.dart';

class InventoryAdjustment extends Equatable {
  const InventoryAdjustment({
    required this.adjustmentId,
    required this.batchId,
    required this.itemId,
    required this.itemName,
    required this.batchNumber,
    required this.direction,
    required this.reason,
    required this.quantity,
    required this.costImpact,
    this.notes,
    required this.performedAt,
    required this.performedBy,
    required this.isVoided,
  });

  final String adjustmentId;
  final String batchId;
  final String itemId;
  final String itemName;
  final String batchNumber;
  final String direction;
  final String reason;
  final double quantity;
  final double costImpact;
  final String? notes;
  final DateTime performedAt;
  final String performedBy;
  final bool isVoided;

  @override
  List<Object?> get props => [
    adjustmentId,
    batchId,
    itemId,
    itemName,
    batchNumber,
    direction,
    reason,
    quantity,
    costImpact,
    notes,
    performedAt,
    performedBy,
    isVoided,
  ];
}
