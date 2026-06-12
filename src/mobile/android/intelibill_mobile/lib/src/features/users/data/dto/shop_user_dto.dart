import 'package:freezed_annotation/freezed_annotation.dart';

part 'shop_user_dto.freezed.dart';
part 'shop_user_dto.g.dart';

@freezed
sealed class ShopUserDto with _$ShopUserDto {
  const factory ShopUserDto({
    @JsonKey(name: 'userId') required String userId,
    @JsonKey(name: 'firstName') required String firstName,
    @JsonKey(name: 'lastName') required String lastName,
    @JsonKey(name: 'email') String? email,
    @JsonKey(name: 'phoneNumber') String? phoneNumber,
    @JsonKey(name: 'role') required String role,
    @JsonKey(name: 'isLoginEnabled') required bool isLoginEnabled,
    @JsonKey(name: 'shopIds') @Default([]) List<String> shopIds,
  }) = _ShopUserDto;

  factory ShopUserDto.fromJson(Map<String, dynamic> json) =>
      _$ShopUserDtoFromJson(json);
}
