import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/repositories/shop_repository.dart';

class GetShopUseCase {
  const GetShopUseCase(this._repository);

  final ShopRepository _repository;

  Future<ShopDetails> call(String shopId) {
    return _repository.getShop(shopId);
  }
}
