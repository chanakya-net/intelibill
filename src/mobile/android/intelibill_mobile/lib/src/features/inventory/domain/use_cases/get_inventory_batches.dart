import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class GetInventoryBatches {
  const GetInventoryBatches(this._repository);

  final InventoryRepository _repository;

  Future<List<InventoryBatch>> call() {
    return _repository.getInventoryBatches();
  }
}
