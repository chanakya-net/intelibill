import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/repositories/shop_user_repository.dart';

class EditShopUser {
  const EditShopUser(this._repository);

  final ShopUserRepository _repository;

  Future<ShopUser> call({
    required String userId,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String role,
    required bool isLoginEnabled,
    required List<String> shopIds,
  }) {
    return _repository.editShopUser(
      userId: userId,
      email: email,
      firstName: firstName,
      lastName: lastName,
      phoneNumber: phoneNumber,
      role: role,
      isLoginEnabled: isLoginEnabled,
      shopIds: shopIds,
    );
  }
}
