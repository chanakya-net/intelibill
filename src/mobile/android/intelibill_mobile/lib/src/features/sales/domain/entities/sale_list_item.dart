import 'package:equatable/equatable.dart';

class SaleListItem extends Equatable {
  const SaleListItem({
    required this.saleId,
    required this.invoiceNumber,
    required this.customerId,
    required this.paymentMethod,
    required this.soldAt,
    required this.paidAmount,
    required this.dueAmount,
    required this.totalBeforeDiscount,
    required this.totalDiscountAmount,
    required this.totalAmount,
    required this.totalTaxAmount,
    required this.customerName,
    required this.customerPhone,
    required this.itemCount,
    required this.returnNumbers,
    required this.status,
    required this.refundAmount,
    required this.dueReductionAmount,
  });

  final String saleId;
  final String invoiceNumber;
  final String? customerId;
  final int paymentMethod;
  final DateTime soldAt;
  final double paidAmount;
  final double dueAmount;
  final double totalBeforeDiscount;
  final double totalDiscountAmount;
  final double totalAmount;
  final double totalTaxAmount;
  final String? customerName;
  final String? customerPhone;
  final int itemCount;
  final List<String> returnNumbers;
  final String status;
  final double refundAmount;
  final double dueReductionAmount;

  @override
  List<Object?> get props => [
    saleId,
    invoiceNumber,
    soldAt,
    totalAmount,
    status,
  ];
}
