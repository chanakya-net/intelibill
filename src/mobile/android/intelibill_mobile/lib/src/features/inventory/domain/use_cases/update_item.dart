import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class UpdateItem {
  const UpdateItem(this._repository);

  final InventoryRepository _repository;

  Future<Item> call({
    required String itemId,
    required String name,
    required String barcode,
    required String uom,
    String? description,
    required bool isActive,
  }) {
    return _repository.updateItem(
      itemId: itemId,
      name: name,
      barcode: barcode,
      uom: uom,
      description: description,
      isActive: isActive,
    );
  }
}
