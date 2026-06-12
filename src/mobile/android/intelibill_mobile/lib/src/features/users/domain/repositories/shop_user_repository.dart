import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';

interface class ShopUserRepository {
  Future<List<ShopUser>> getShopUsers() {
    throw UnimplementedError();
  }

  Future<ShopUser> addShopUser({
    required List<String> shopIds,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String password,
    required String confirmPassword,
    required String role,
  }) {
    throw UnimplementedError();
  }

  Future<ShopUser> editShopUser({
    required String userId,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String role,
    required bool isLoginEnabled,
    required List<String> shopIds,
  }) {
    throw UnimplementedError();
  }
}
