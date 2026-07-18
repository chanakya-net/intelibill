import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

class PurchaseOrder extends Equatable {
  const PurchaseOrder({
    required this.purchaseOrderId,
    required this.purchaseOrderNumber,
    required this.status,
    this.supplierId,
    this.orderDate,
    this.expectedDeliveryDate,
    this.supplierReferenceNumber,
    this.notes,
    required this.lines,
    required this.expectedTotal,
    required this.createdAt,
    this.supplierName,
    this.supplierReference,
    this.receivedQuantity = 0,
  });

  final String purchaseOrderId;
  final String purchaseOrderNumber;
  final PurchaseOrderStatus status;
  final String? supplierId;
  final DateTime? orderDate;
  final DateTime? expectedDeliveryDate;
  final String? supplierReferenceNumber;
  final String? notes;
  final List<PurchaseOrderLine> lines;
  final double expectedTotal;
  final DateTime createdAt;
  final String? supplierName;
  final String? supplierReference;
  final int receivedQuantity;

  int get expectedQuantity =>
      lines.fold(0, (sum, line) => sum + line.expectedQuantity);

  int get remainingQuantity =>
      lines.fold(0, (sum, line) => sum + line.remainingQuantity);

  @override
  List<Object?> get props => [
    purchaseOrderId,
    purchaseOrderNumber,
    status,
    supplierId,
    orderDate,
    expectedDeliveryDate,
    supplierReferenceNumber,
    notes,
    lines,
    expectedTotal,
    createdAt,
    supplierName,
    supplierReference,
    receivedQuantity,
  ];
}
