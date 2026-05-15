import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class GetItems {
  const GetItems(this._repository);

  final InventoryRepository _repository;

  Future<List<Item>> call() {
    return _repository.getItems();
  }
}
