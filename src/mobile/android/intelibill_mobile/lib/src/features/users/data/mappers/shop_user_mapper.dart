import 'package:intelibill_mobile/src/features/users/data/dto/shop_user_dto.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';

class ShopUserMapper {
  static ShopUser toDomain(ShopUserDto dto) {
    return ShopUser(
      userId: dto.userId,
      firstName: dto.firstName,
      lastName: dto.lastName,
      email: dto.email,
      phoneNumber: dto.phoneNumber,
      role: dto.role,
      isLoginEnabled: dto.isLoginEnabled,
      shopIds: dto.shopIds,
    );
  }
}
