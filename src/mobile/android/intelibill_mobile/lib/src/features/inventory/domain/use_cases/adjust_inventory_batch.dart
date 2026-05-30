import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class AdjustInventoryBatch {
  const AdjustInventoryBatch(this._repository);

  final InventoryRepository _repository;

  Future<void> call({
    required String batchId,
    required String direction,
    required String reason,
    required double quantity,
    DateTime? performedAt,
    String? notes,
  }) {
    return _repository.adjustInventoryBatch(
      batchId: batchId,
      direction: direction,
      reason: reason,
      quantity: quantity,
      performedAt: performedAt,
      notes: notes,
    );
  }
}
