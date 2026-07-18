import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt_line.dart';

class PurchaseOrderReceipt extends Equatable {
  const PurchaseOrderReceipt({
    required this.receiptId,
    required this.receiptNumber,
    required this.receivedAt,
    this.referenceNumber,
    this.notes,
    required this.receivedByUserId,
    this.receivedByDisplayName,
    required this.lines,
  });

  final String receiptId;
  final String receiptNumber;
  final DateTime receivedAt;
  final String? referenceNumber;
  final String? notes;
  final String receivedByUserId;
  final String? receivedByDisplayName;
  final List<PurchaseOrderReceiptLine> lines;

  @override
  List<Object?> get props => [
    receiptId,
    receiptNumber,
    receivedAt,
    referenceNumber,
    notes,
    receivedByUserId,
    receivedByDisplayName,
    lines,
  ];
}
