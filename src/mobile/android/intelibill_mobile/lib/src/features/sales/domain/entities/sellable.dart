import 'package:equatable/equatable.dart';

class Sellable extends Equatable {
  const Sellable({
    required this.id,
    required this.kind,
    required this.name,
    required this.stock,
    required this.price,
    this.barcode,
    this.batchNumber,
    this.mrp = 0,
    this.taxRatePercent = 0,
    this.taxIncluded = false,
    this.purchaseTaxIncluded = false,
    this.expiryDate,
  });

  final String id;
  final String kind;
  final String name;
  final String? barcode;
  final String? batchNumber;
  final double stock;
  final double price;
  final double mrp;
  final double taxRatePercent;
  final bool taxIncluded;
  final bool purchaseTaxIncluded;
  final DateTime? expiryDate;

  bool get isGoods => kind == 'Goods';
  bool get isService => kind == 'Service';

  @override
  List<Object?> get props => [
    id,
    kind,
    name,
    barcode,
    batchNumber,
    stock,
    price,
    mrp,
    taxRatePercent,
    taxIncluded,
    purchaseTaxIncluded,
    expiryDate,
  ];
}

class NewSaleCartLine extends Equatable {
  const NewSaleCartLine({required this.sellable, required this.quantity});

  final Sellable sellable;
  final int quantity;

  double get lineTotal => quantity * sellable.price;

  NewSaleCartLine copyWith({int? quantity}) {
    return NewSaleCartLine(
      sellable: sellable,
      quantity: quantity ?? this.quantity,
    );
  }

  @override
  List<Object?> get props => [sellable, quantity];
}
