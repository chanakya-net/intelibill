import 'package:freezed_annotation/freezed_annotation.dart';

part 'refresh_token_request_dto.freezed.dart';
part 'refresh_token_request_dto.g.dart';

@freezed
sealed class RefreshTokenRequestDto with _$RefreshTokenRequestDto {
  const factory RefreshTokenRequestDto({
    @JsonKey(name: 'refreshToken') required String refreshToken,
  }) = _RefreshTokenRequestDto;

  factory RefreshTokenRequestDto.fromJson(Map<String, dynamic> json) =>
      _$RefreshTokenRequestDtoFromJson(json);
}
