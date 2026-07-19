import 'package:intelibill_mobile/src/features/inventory/domain/entities/generated_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class GenerateItemBarcode {
  const GenerateItemBarcode(this._repository);

  final InventoryRepository _repository;

  Future<GeneratedItemBarcode> call() {
    return _repository.generateItemBarcode();
  }
}
