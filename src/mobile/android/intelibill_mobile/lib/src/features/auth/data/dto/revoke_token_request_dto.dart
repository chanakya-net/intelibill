import 'package:freezed_annotation/freezed_annotation.dart';

part 'revoke_token_request_dto.freezed.dart';
part 'revoke_token_request_dto.g.dart';

@freezed
sealed class RevokeTokenRequestDto with _$RevokeTokenRequestDto {
  const factory RevokeTokenRequestDto({
    @JsonKey(name: 'refreshToken') required String refreshToken,
  }) = _RevokeTokenRequestDto;

  factory RevokeTokenRequestDto.fromJson(Map<String, dynamic> json) =>
      _$RevokeTokenRequestDtoFromJson(json);
}
