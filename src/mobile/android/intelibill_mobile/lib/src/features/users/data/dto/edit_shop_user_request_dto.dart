import 'package:freezed_annotation/freezed_annotation.dart';

part 'edit_shop_user_request_dto.freezed.dart';
part 'edit_shop_user_request_dto.g.dart';

@freezed
sealed class EditShopUserRequestDto with _$EditShopUserRequestDto {
  const factory EditShopUserRequestDto({
    @JsonKey(name: 'email') required String email,
    @JsonKey(name: 'firstName') required String firstName,
    @JsonKey(name: 'lastName') required String lastName,
    @JsonKey(name: 'phoneNumber') required String phoneNumber,
    @JsonKey(name: 'role') required String role,
    @JsonKey(name: 'isLoginEnabled') required bool isLoginEnabled,
    @JsonKey(name: 'shopIds') required List<String> shopIds,
  }) = _EditShopUserRequestDto;

  factory EditShopUserRequestDto.fromJson(Map<String, dynamic> json) =>
      _$EditShopUserRequestDtoFromJson(json);
}
