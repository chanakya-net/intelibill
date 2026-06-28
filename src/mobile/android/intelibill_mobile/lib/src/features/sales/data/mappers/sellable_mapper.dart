import 'package:intelibill_mobile/src/features/sales/data/dto/sellable_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

class SellableMapper {
  const SellableMapper._();

  static Sellable toDomain(SellableDto dto) {
    final isGoods = dto.kind == 'Goods';
    final isService = dto.kind == 'Service';

    return Sellable(
      id:
          (isGoods
              ? dto.inventoryBatchId
              : (isService ? dto.serviceId : null)) ??
          '',
      kind: dto.kind,
      name: isGoods ? dto.itemName ?? '' : (isService ? dto.name ?? '' : ''),
      barcode: isGoods ? dto.barcode : (isService ? dto.code : null),
      batchNumber: dto.batchNumber,
      stock: isGoods ? dto.quantity : 0,
      price: isGoods ? dto.salesPrice : (isService ? dto.price : 0),
      mrp: dto.mrp,
      taxRatePercent: dto.taxRatePercent,
      taxIncluded: dto.taxIncluded,
      purchaseTaxIncluded: dto.purchaseTaxIncluded,
      expiryDate: dto.expiryDate,
    );
  }
}
