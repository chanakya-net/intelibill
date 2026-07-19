import 'package:intelibill_mobile/src/features/inventory/data/dto/generate_item_barcode_response_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/generated_item_barcode.dart';

class GeneratedItemBarcodeMapper {
  static GeneratedItemBarcode toDomain(GenerateItemBarcodeResponseDto dto) {
    return GeneratedItemBarcode(barcode: dto.barcode);
  }
}
