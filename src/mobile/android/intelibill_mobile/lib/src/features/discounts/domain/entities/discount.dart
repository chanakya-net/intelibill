import 'package:equatable/equatable.dart';

enum DiscountType { fixed, percentage }

class Discount extends Equatable {
  const Discount({
    required this.discountId,
    required this.name,
    required this.discountType,
    required this.discountValue,
    required this.batchPercentage,
    required this.isEnabled,
    required this.createdAt,
  });

  final String discountId;
  final String name;
  final DiscountType discountType;
  final double discountValue;
  final double? batchPercentage;
  final bool isEnabled;
  final DateTime createdAt;

  @override
  List<Object?> get props => [
    discountId,
    name,
    discountType,
    discountValue,
    batchPercentage,
    isEnabled,
    createdAt,
  ];
}
