import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/repositories/shop_repository.dart';

class UpdateShopUseCase {
  const UpdateShopUseCase(this._repository);

  final ShopRepository _repository;

  Future<ShopDetails> call(String shopId, UpdateShopRequest request) {
    return _repository.updateShop(shopId, request);
  }
}
