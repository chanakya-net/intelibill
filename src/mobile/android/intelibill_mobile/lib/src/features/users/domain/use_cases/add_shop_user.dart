import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/repositories/shop_user_repository.dart';

class AddShopUser {
  const AddShopUser(this._repository);

  final ShopUserRepository _repository;

  Future<ShopUser> call({
    required List<String> shopIds,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String password,
    required String confirmPassword,
    required String role,
  }) {
    return _repository.addShopUser(
      shopIds: shopIds,
      email: email,
      firstName: firstName,
      lastName: lastName,
      phoneNumber: phoneNumber,
      password: password,
      confirmPassword: confirmPassword,
      role: role,
    );
  }
}
