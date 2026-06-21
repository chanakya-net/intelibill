import 'package:freezed_annotation/freezed_annotation.dart';

part 'create_service_request_dto.freezed.dart';
part 'create_service_request_dto.g.dart';

@freezed
sealed class CreateServiceRequestDto with _$CreateServiceRequestDto {
  const factory CreateServiceRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'price') required double price,
    @JsonKey(name: 'hsnCode') String? hsnCode,
    @JsonKey(name: 'taxRatePercent') required double taxRatePercent,
    @JsonKey(name: 'taxIncluded') required bool taxIncluded,
    @JsonKey(name: 'isActive') required bool isActive,
  }) = _CreateServiceRequestDto;

  factory CreateServiceRequestDto.fromJson(Map<String, dynamic> json) =>
      _$CreateServiceRequestDtoFromJson(json);
}
