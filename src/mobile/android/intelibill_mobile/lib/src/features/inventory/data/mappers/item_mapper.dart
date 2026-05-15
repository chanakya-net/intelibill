import 'package:intelibill_mobile/src/features/inventory/data/dto/item_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';

class ItemMapper {
  static Item toDomain(ItemDto dto) {
    return Item(
      itemId: dto.id,
      name: dto.name,
      barcode: dto.barcode,
      description: dto.description,
      uom: dto.uom,
      isActive: dto.isActive,
      currentStock: dto.currentStock,
    );
  }
}
