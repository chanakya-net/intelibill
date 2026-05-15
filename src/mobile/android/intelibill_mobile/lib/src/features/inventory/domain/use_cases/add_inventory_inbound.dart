import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class AddInventoryInbound {
  const AddInventoryInbound(this._repository);

  final InventoryRepository _repository;

  Future<void> call({
    required String itemName,
    required String barcode,
    required String uom,
    String? batchNumber,
    required double quantity,
    required double costPrice,
    required double mrp,
    required double salesPrice,
    double taxRate = 0.0,
    bool taxIncluded = false,
    DateTime? expiryDate,
    DateTime? manufacturingDate,
    String? referenceNumber,
    String? notes,
  }) {
    return _repository.addInventoryInbound(
      itemName: itemName,
      barcode: barcode,
      uom: uom,
      batchNumber: batchNumber,
      quantity: quantity,
      costPrice: costPrice,
      mrp: mrp,
      salesPrice: salesPrice,
      taxRate: taxRate,
      taxIncluded: taxIncluded,
      expiryDate: expiryDate,
      manufacturingDate: manufacturingDate,
      referenceNumber: referenceNumber,
      notes: notes,
    );
  }
}
