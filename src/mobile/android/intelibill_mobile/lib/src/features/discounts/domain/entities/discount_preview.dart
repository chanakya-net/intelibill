import 'package:equatable/equatable.dart';

class DiscountPreview extends Equatable {
  const DiscountPreview({
    required this.totalCostReduction,
    required this.error,
    required this.estimatedProfit,
  });

  final double totalCostReduction;
  final String? error;
  final double estimatedProfit;

  @override
  List<Object?> get props => [totalCostReduction, error, estimatedProfit];
}
