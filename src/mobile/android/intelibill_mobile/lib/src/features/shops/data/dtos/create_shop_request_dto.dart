import 'package:freezed_annotation/freezed_annotation.dart';

part 'create_shop_request_dto.freezed.dart';
part 'create_shop_request_dto.g.dart';

@freezed
sealed class CreateShopRequestDto with _$CreateShopRequestDto {
  const factory CreateShopRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'address') required String address,
    @JsonKey(name: 'city') required String city,
    @JsonKey(name: 'state') required String state,
    @JsonKey(name: 'pincode') required String pincode,
    @JsonKey(name: 'contactPerson') String? contactPerson,
    @JsonKey(name: 'mobileNumber') String? mobileNumber,
    @JsonKey(name: 'gstNumber') String? gstNumber,
  }) = _CreateShopRequestDto;

  factory CreateShopRequestDto.fromJson(Map<String, dynamic> json) =>
      _$CreateShopRequestDtoFromJson(json);
}
