import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_batch_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';

class InventoryBatchMapper {
  static InventoryBatch toDomain(InventoryBatchDto dto) {
    return InventoryBatch(
      batchId: dto.id,
      itemId: dto.itemId,
      itemName: dto.itemName,
      itemBarcode: dto.barcode,
      itemUom: dto.itemUom,
      batchNumber: dto.batchNumber,
      quantity: dto.quantity,
      costPrice: dto.costPrice,
      mrp: dto.mrp,
      salesPrice: dto.salesPrice,
      taxRate: dto.taxRatePercent,
      taxIncluded: dto.taxIncluded,
      expiryDate: dto.expiryDate,
      manufacturingDate: dto.manufacturingDate,
      referenceNumber: dto.referenceNumber,
      notes: dto.notes,
      supplierId: dto.supplierId,
      supplierName: dto.supplierName,
      isVoided: dto.isVoided,
      createdAt: dto.createdAt,
    );
  }
}
