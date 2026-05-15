import 'package:equatable/equatable.dart';

class Item extends Equatable {
  const Item({
    required this.itemId,
    required this.name,
    required this.barcode,
    this.description,
    required this.uom,
    required this.isActive,
    required this.currentStock,
  });

  final String itemId;
  final String name;
  final String barcode;
  final String? description;
  final String uom;
  final bool isActive;
  final double currentStock;

  @override
  List<Object?> get props => [
    itemId,
    name,
    barcode,
    description,
    uom,
    isActive,
    currentStock,
  ];
}
