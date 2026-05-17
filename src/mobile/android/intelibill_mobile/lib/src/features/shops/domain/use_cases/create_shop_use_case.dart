import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/repositories/shop_repository.dart';

class CreateShopUseCase {
  const CreateShopUseCase(this._repository);

  final ShopRepository _repository;

  Future<AuthSession> call(CreateShopRequest request) {
    return _repository.createShop(request);
  }
}
