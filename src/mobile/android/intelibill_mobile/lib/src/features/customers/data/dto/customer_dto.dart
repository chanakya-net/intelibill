import 'package:freezed_annotation/freezed_annotation.dart';

part 'customer_dto.freezed.dart';
part 'customer_dto.g.dart';

@freezed
sealed class CustomerDto with _$CustomerDto {
  const factory CustomerDto({
    @JsonKey(name: 'customerId') required String customerId,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'phoneNumber') required String phoneNumber,
    @JsonKey(name: 'address') String? address,
    @JsonKey(name: 'isActive') required bool isActive,
    @JsonKey(name: 'outstandingDue') @Default(0.0) double outstandingDue,
  }) = _CustomerDto;

  factory CustomerDto.fromJson(Map<String, dynamic> json) =>
      _$CustomerDtoFromJson(json);
}
