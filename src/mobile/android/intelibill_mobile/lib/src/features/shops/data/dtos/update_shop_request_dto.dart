import 'package:freezed_annotation/freezed_annotation.dart';

part 'update_shop_request_dto.freezed.dart';
part 'update_shop_request_dto.g.dart';

@freezed
sealed class UpdateShopRequestDto with _$UpdateShopRequestDto {
  const factory UpdateShopRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'address') required String address,
    @JsonKey(name: 'city') required String city,
    @JsonKey(name: 'state') required String state,
    @JsonKey(name: 'pincode') required String pincode,
    @JsonKey(name: 'contactPerson') String? contactPerson,
    @JsonKey(name: 'mobileNumber') String? mobileNumber,
    @JsonKey(name: 'gstNumber') String? gstNumber,
  }) = _UpdateShopRequestDto;

  factory UpdateShopRequestDto.fromJson(Map<String, dynamic> json) =>
      _$UpdateShopRequestDtoFromJson(json);
}
