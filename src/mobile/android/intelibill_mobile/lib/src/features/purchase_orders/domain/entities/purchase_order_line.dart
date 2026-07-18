import 'package:equatable/equatable.dart';

class PurchaseOrderLine extends Equatable {
  const PurchaseOrderLine({
    required this.lineId,
    required this.itemId,
    required this.description,
    required this.expectedQuantity,
    required this.receivedQuantity,
    required this.remainingQuantity,
    required this.unitCost,
    required this.lineTotal,
  });

  final String lineId;
  final String itemId;
  final String description;
  final int expectedQuantity;
  final int receivedQuantity;
  final int remainingQuantity;
  final double unitCost;
  final double lineTotal;

  @override
  List<Object?> get props => [
    lineId,
    itemId,
    description,
    expectedQuantity,
    receivedQuantity,
    remainingQuantity,
    unitCost,
    lineTotal,
  ];
}
