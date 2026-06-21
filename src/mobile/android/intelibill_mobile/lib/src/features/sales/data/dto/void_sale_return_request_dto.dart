import 'package:freezed_annotation/freezed_annotation.dart';

part 'void_sale_return_request_dto.freezed.dart';
part 'void_sale_return_request_dto.g.dart';

@freezed
sealed class VoidSaleReturnRequestDto with _$VoidSaleReturnRequestDto {
  const factory VoidSaleReturnRequestDto({
    @JsonKey(name: 'reason') required String reason,
  }) = _VoidSaleReturnRequestDto;

  factory VoidSaleReturnRequestDto.fromJson(Map<String, dynamic> json) =>
      _$VoidSaleReturnRequestDtoFromJson(json);
}
