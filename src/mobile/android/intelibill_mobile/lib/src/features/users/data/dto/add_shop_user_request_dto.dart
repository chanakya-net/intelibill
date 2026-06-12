import 'package:freezed_annotation/freezed_annotation.dart';

part 'add_shop_user_request_dto.freezed.dart';
part 'add_shop_user_request_dto.g.dart';

@freezed
sealed class AddShopUserRequestDto with _$AddShopUserRequestDto {
  const factory AddShopUserRequestDto({
    @JsonKey(name: 'shopIds') required List<String> shopIds,
    @JsonKey(name: 'email') required String email,
    @JsonKey(name: 'firstName') required String firstName,
    @JsonKey(name: 'lastName') required String lastName,
    @JsonKey(name: 'phoneNumber') required String phoneNumber,
    @JsonKey(name: 'password') required String password,
    @JsonKey(name: 'confirmPassword') required String confirmPassword,
    @JsonKey(name: 'role') required String role,
  }) = _AddShopUserRequestDto;

  factory AddShopUserRequestDto.fromJson(Map<String, dynamic> json) =>
      _$AddShopUserRequestDtoFromJson(json);
}
