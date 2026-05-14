import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_user_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/user_shop_dto.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

class AuthMapper {
  static AuthSession toDomain(AuthResultDto dto, {bool rememberMe = false}) {
    return AuthSession(
      accessToken: dto.accessToken,
      refreshToken: dto.refreshToken,
      accessTokenExpiresAt: dto.accessTokenExpiresAt,
      refreshTokenExpiresAt: dto.refreshTokenExpiresAt,
      user: _authUserToDomain(dto.user),
      activeShopId: dto.activeShopId,
      shops: dto.shops?.map(_userShopToDomain).toList(),
      rememberMe: rememberMe,
    );
  }

  static AuthUser _authUserToDomain(AuthUserDto dto) {
    return AuthUser(
      id: dto.id,
      email: dto.email,
      phoneNumber: dto.phoneNumber,
      firstName: dto.firstName,
      lastName: dto.lastName,
      language: dto.language ?? 'en-IN',
    );
  }

  static UserShop _userShopToDomain(UserShopDto dto) {
    return UserShop(
      shopId: dto.shopId,
      shopName: dto.shopName,
      role: dto.role,
      isDefault: dto.isDefault,
      lastUsedAt: dto.lastUsedAt,
    );
  }
}
