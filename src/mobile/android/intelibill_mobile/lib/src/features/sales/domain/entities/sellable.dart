import 'package:equatable/equatable.dart';

class InstantDiscountType {
  static const int none = 0;
  static const int percentage = 1;
  static const int flat = 2;
}

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
  const NewSaleCartLine({
    required this.sellable,
    required this.quantity,
    this.unitPrice,
    this.itemDiscountType = InstantDiscountType.none,
    this.itemDiscountValue = 0,
  });
  static const _unset = Object();
  static const _unsetType = Object();

  final Sellable sellable;
  final double quantity;
  final double? unitPrice;
  final int itemDiscountType;
  final double itemDiscountValue;

  double get effectiveUnitPrice => unitPrice ?? sellable.price;

  double get lineTotal => quantity * effectiveUnitPrice;

  NewSaleCartLine copyWith({
    double? quantity,
    Object? unitPrice = _unset,
    Object? itemDiscountType = _unsetType,
    Object? itemDiscountValue = _unset,
  }) {
    return NewSaleCartLine(
      sellable: sellable,
      quantity: quantity ?? this.quantity,
      unitPrice: identical(unitPrice, _unset)
          ? this.unitPrice
          : unitPrice as double?,
      itemDiscountType: identical(itemDiscountType, _unsetType)
          ? this.itemDiscountType
          : itemDiscountType! as int,
      itemDiscountValue: identical(itemDiscountValue, _unset)
          ? this.itemDiscountValue
          : itemDiscountValue! as double,
    );
  }

  @override
  List<Object?> get props => [
    sellable,
    quantity,
    unitPrice,
    itemDiscountType,
    itemDiscountValue,
  ];
}
