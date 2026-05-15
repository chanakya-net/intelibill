import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_adjustment_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';

class InventoryAdjustmentMapper {
  static InventoryAdjustment toDomain(InventoryAdjustmentDto dto) {
    return InventoryAdjustment(
      adjustmentId: dto.adjustmentId,
      batchId: dto.batchId,
      itemId: dto.itemId,
      itemName: dto.itemName,
      batchNumber: dto.batchNumber,
      direction: dto.direction,
      reason: dto.reason,
      quantity: dto.quantity,
      costImpact: dto.costImpact,
      notes: dto.notes,
      performedAt: dto.performedAt,
      performedBy: dto.performedByDisplayName,
      isVoided: dto.isVoided,
    );
  }
}
