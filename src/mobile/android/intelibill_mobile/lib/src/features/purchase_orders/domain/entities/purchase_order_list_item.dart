import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

class PurchaseOrderListItem extends Equatable {
  const PurchaseOrderListItem({
    required this.purchaseOrderId,
    required this.purchaseOrderNumber,
    required this.status,
    required this.supplierName,
    required this.supplierReference,
    required this.lineCount,
    required this.expectedQuantity,
    required this.receivedQuantity,
    required this.expectedTotal,
    required this.createdAt,
  });

  final String purchaseOrderId;
  final String purchaseOrderNumber;
  final PurchaseOrderStatus status;
  final String? supplierName;
  final String? supplierReference;
  final int lineCount;
  final int expectedQuantity;
  final int receivedQuantity;
  final double expectedTotal;
  final DateTime createdAt;

  @override
  List<Object?> get props => [
    purchaseOrderId,
    purchaseOrderNumber,
    status,
    supplierName,
    supplierReference,
    lineCount,
    expectedQuantity,
    receivedQuantity,
    expectedTotal,
    createdAt,
  ];
}
