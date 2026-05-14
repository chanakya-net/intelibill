import 'package:freezed_annotation/freezed_annotation.dart';

part 'create_customer_request_dto.freezed.dart';
part 'create_customer_request_dto.g.dart';

@freezed
sealed class CreateCustomerRequestDto with _$CreateCustomerRequestDto {
  const factory CreateCustomerRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'phoneNumber') required String phoneNumber,
    @JsonKey(name: 'address') String? address,
    @JsonKey(name: 'isActive') required bool isActive,
  }) = _CreateCustomerRequestDto;

  factory CreateCustomerRequestDto.fromJson(Map<String, dynamic> json) =>
      _$CreateCustomerRequestDtoFromJson(json);
}
