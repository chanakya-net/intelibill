import 'package:freezed_annotation/freezed_annotation.dart';

part 'update_service_request_dto.freezed.dart';
part 'update_service_request_dto.g.dart';

@freezed
sealed class UpdateServiceRequestDto with _$UpdateServiceRequestDto {
  const factory UpdateServiceRequestDto({
    @JsonKey(name: 'name') required String name,
    @JsonKey(name: 'description') String? description,
    @JsonKey(name: 'price') required double price,
    @JsonKey(name: 'hsnCode') String? hsnCode,
    @JsonKey(name: 'taxRatePercent') required double taxRatePercent,
    @JsonKey(name: 'taxIncluded') required bool taxIncluded,
  }) = _UpdateServiceRequestDto;

  factory UpdateServiceRequestDto.fromJson(Map<String, dynamic> json) =>
      _$UpdateServiceRequestDtoFromJson(json);
}
