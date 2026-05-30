import 'package:equatable/equatable.dart';

class AuthUser extends Equatable {
  const AuthUser({
    required this.id,
    required this.email,
    required this.phoneNumber,
    required this.firstName,
    required this.lastName,
    required this.language,
  });

  final String id;
  final String? email;
  final String? phoneNumber;
  final String firstName;
  final String lastName;
  final String language;

  @override
  List<Object?> get props => [
    id,
    email,
    phoneNumber,
    firstName,
    lastName,
    language,
  ];
}

class UserShop extends Equatable {
  const UserShop({
    required this.shopId,
    required this.shopName,
    required this.role,
    required this.isDefault,
    required this.lastUsedAt,
  });

  final String shopId;
  final String shopName;
  final String role;
  final bool isDefault;
  final DateTime? lastUsedAt;

  @override
  List<Object?> get props => [shopId, shopName, role, isDefault, lastUsedAt];
}

class AuthSession extends Equatable {
  const AuthSession({
    required this.accessToken,
    required this.refreshToken,
    required this.accessTokenExpiresAt,
    required this.refreshTokenExpiresAt,
    required this.user,
    required this.activeShopId,
    required this.shops,
    required this.rememberMe,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessTokenExpiresAt;
  final DateTime refreshTokenExpiresAt;
  final AuthUser user;
  final String? activeShopId;
  final List<UserShop>? shops;
  final bool rememberMe;

  @override
  List<Object?> get props => [
    accessToken,
    refreshToken,
    accessTokenExpiresAt,
    refreshTokenExpiresAt,
    user,
    activeShopId,
    shops,
    rememberMe,
  ];
}
