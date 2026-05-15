import 'package:equatable/equatable.dart';

class ProductDetails extends Equatable {
  const ProductDetails({
    required this.name,
    required this.description,
    required this.uom,
    required this.costPrice,
    required this.mrp,
    required this.salesPrice,
    this.supplierId,
    this.supplierName,
    this.taxIncluded,
    this.taxRatePercent,
  });

  final String name;
  final String description;
  final String uom;
  final double costPrice;
  final double mrp;
  final double salesPrice;
  final String? supplierId;
  final String? supplierName;
  final bool? taxIncluded;
  final double? taxRatePercent;

  @override
  List<Object?> get props => [
    name,
    description,
    uom,
    costPrice,
    mrp,
    salesPrice,
    supplierId,
    supplierName,
    taxIncluded,
    taxRatePercent,
  ];
}
