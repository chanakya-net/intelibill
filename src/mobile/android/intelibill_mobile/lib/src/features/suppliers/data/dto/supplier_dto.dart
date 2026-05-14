import 'package:freezed_annotation/freezed_annotation.dart';

part 'supplier_dto.freezed.dart';
part 'supplier_dto.g.dart';

@freezed
sealed class SupplierDto with _$SupplierDto {
  const factory SupplierDto({
    @JsonKey(name: 'supplierId') required String supplierId,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'contactPersonName') String? contactPersonName,
    @JsonKey(name: 'contactPersonPhone') String? contactPersonPhone,
    @JsonKey(name: 'address') String? address,
    @JsonKey(name: 'city') String? city,
    @JsonKey(name: 'state') String? state,
    @JsonKey(name: 'pin') String? pin,
    @JsonKey(name: 'isSystem') required bool isSystem,
    @JsonKey(name: 'isActive') required bool isActive,
    @JsonKey(name: 'isPreferred') required bool isPreferred,
    @JsonKey(name: 'balanceDue') @Default(0.0) double balanceDue,
  }) = _SupplierDto;

  factory SupplierDto.fromJson(Map<String, dynamic> json) =>
      _$SupplierDtoFromJson(json);
}
