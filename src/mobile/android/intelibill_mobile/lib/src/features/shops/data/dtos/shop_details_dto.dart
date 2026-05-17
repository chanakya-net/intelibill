import 'package:freezed_annotation/freezed_annotation.dart';

part 'shop_details_dto.freezed.dart';
part 'shop_details_dto.g.dart';

@freezed
sealed class ShopDetailsDto with _$ShopDetailsDto {
  const factory ShopDetailsDto({
    @JsonKey(name: 'shopId') required String shopId,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'address') required String address,
    @JsonKey(name: 'city') required String city,
    @JsonKey(name: 'state') required String state,
    @JsonKey(name: 'pincode') required String pincode,
    @JsonKey(name: 'contactPerson') String? contactPerson,
    @JsonKey(name: 'mobileNumber') String? mobileNumber,
    @JsonKey(name: 'gstNumber') String? gstNumber,
    @JsonKey(name: 'bankName') String? bankName,
    @JsonKey(name: 'bankAccountNumber') String? bankAccountNumber,
    @JsonKey(name: 'bankAccountType') String? bankAccountType,
    @JsonKey(name: 'ifscCode') String? ifscCode,
    @JsonKey(name: 'accountHolderName') String? accountHolderName,
  }) = _ShopDetailsDto;

  factory ShopDetailsDto.fromJson(Map<String, dynamic> json) =>
      _$ShopDetailsDtoFromJson(json);
}
