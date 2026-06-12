import 'package:equatable/equatable.dart';

class ShopUser extends Equatable {
  const ShopUser({
    required this.userId,
    required this.firstName,
    required this.lastName,
    this.email,
    this.phoneNumber,
    required this.role,
    required this.isLoginEnabled,
    required this.shopIds,
  });

  final String userId;
  final String firstName;
  final String lastName;
  final String? email;
  final String? phoneNumber;
  final String role;
  final bool isLoginEnabled;
  final List<String> shopIds;

  String get fullName => '$firstName $lastName'.trim();

  bool get isOwner => role.trim().toLowerCase() == 'owner';

  @override
  List<Object?> get props => [
    userId,
    firstName,
    lastName,
    email,
    phoneNumber,
    role,
    isLoginEnabled,
    shopIds,
  ];
}
