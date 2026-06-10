import 'package:equatable/equatable.dart';

class SalesHistorySummary extends Equatable {
  const SalesHistorySummary({
    required this.periodSales,
    required this.invoiceCount,
    required this.refundAmount,
  });

  final double periodSales;
  final int invoiceCount;
  final double refundAmount;

  @override
  List<Object?> get props => [periodSales, invoiceCount, refundAmount];
}
