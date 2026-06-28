import 'package:freezed_annotation/freezed_annotation.dart';

part 'service_dto.freezed.dart';
part 'service_dto.g.dart';

@freezed
sealed class ServiceDto with _$ServiceDto {
  const factory ServiceDto({
    @JsonKey(name: 'serviceId') required String serviceId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'price') @Default(0.0) double price,
    @JsonKey(name: 'hsnCode') String? hsnCode,
    @JsonKey(name: 'taxRatePercent') @Default(0.0) double taxRatePercent,
    @JsonKey(name: 'taxIncluded') required bool taxIncluded,
    @JsonKey(name: 'isActive') required bool isActive,
  }) = _ServiceDto;

  factory ServiceDto.fromJson(Map<String, dynamic> json) =>
      _$ServiceDtoFromJson(json);
}
