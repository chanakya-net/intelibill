import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class GetAdjustmentHistory {
  const GetAdjustmentHistory(this._repository);

  final InventoryRepository _repository;

  Future<({List<InventoryAdjustment> items, bool hasMore})> call({
    required int pageNumber,
    required int pageSize,
  }) {
    return _repository.getAdjustmentHistory(
      pageNumber: pageNumber,
      pageSize: pageSize,
    );
  }
}
