import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class GetProductDetails {
  const GetProductDetails(this._repository);

  final InventoryRepository _repository;

  Future<ProductDetails> call({String? name, String? barcode}) {
    return _repository.getProductDetails(name: name, barcode: barcode);
  }
}
