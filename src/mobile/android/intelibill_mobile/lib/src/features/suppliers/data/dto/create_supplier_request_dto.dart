import 'package:freezed_annotation/freezed_annotation.dart';

part 'create_supplier_request_dto.freezed.dart';
part 'create_supplier_request_dto.g.dart';

@freezed
sealed class CreateSupplierRequestDto with _$CreateSupplierRequestDto {
  const factory CreateSupplierRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'contactPersonName') String? contactPersonName,
    @JsonKey(name: 'contactPersonPhone') String? contactPersonPhone,
    @JsonKey(name: 'address') required String address,
    @JsonKey(name: 'city') required String city,
    @JsonKey(name: 'state') required String state,
    @JsonKey(name: 'pin') required String pin,
    @JsonKey(name: 'isActive') required bool isActive,
    @JsonKey(name: 'isPreferred') required bool isPreferred,
  }) = _CreateSupplierRequestDto;

  factory CreateSupplierRequestDto.fromJson(Map<String, dynamic> json) =>
      _$CreateSupplierRequestDtoFromJson(json);
}
