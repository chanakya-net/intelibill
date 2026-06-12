import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/repositories/shop_user_repository.dart';

class GetShopUsers {
  const GetShopUsers(this._repository);

  final ShopUserRepository _repository;

  Future<List<ShopUser>> call() => _repository.getShopUsers();
}
