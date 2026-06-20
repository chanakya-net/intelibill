import 'package:intelibill_mobile/src/features/sales/data/dto/sellable_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

class SellableMapper {
  const SellableMapper._();

  static Sellable toDomain(SellableDto dto) {
    final isGoods = dto.kind == 'Goods';

    return Sellable(
      id: (isGoods ? dto.inventoryBatchId : dto.serviceId) ?? '',
      kind: dto.kind,
      name: isGoods ? dto.itemName ?? '' : dto.name ?? '',
      barcode: isGoods ? dto.barcode : dto.code,
      batchNumber: dto.batchNumber,
      stock: isGoods ? dto.quantity : 0,
      price: isGoods ? dto.salesPrice : dto.price,
      mrp: dto.mrp,
      taxRatePercent: dto.taxRatePercent,
      taxIncluded: dto.taxIncluded,
      purchaseTaxIncluded: dto.purchaseTaxIncluded,
      expiryDate: dto.expiryDate,
    );
  }
}
