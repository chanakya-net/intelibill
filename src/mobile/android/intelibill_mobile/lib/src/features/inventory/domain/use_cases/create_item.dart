import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class CreateItem {
  const CreateItem(this._repository);

  final InventoryRepository _repository;

  Future<Item> call({
    required String name,
    required String barcode,
    required String uom,
    String? description,
  }) {
    return _repository.createItem(
      name: name,
      barcode: barcode,
      uom: uom,
      description: description,
    );
  }
}
