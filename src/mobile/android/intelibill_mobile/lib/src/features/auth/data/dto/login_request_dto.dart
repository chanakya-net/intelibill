import 'package:freezed_annotation/freezed_annotation.dart';

part 'login_request_dto.freezed.dart';
part 'login_request_dto.g.dart';

@freezed
sealed class LoginRequestDto with _$LoginRequestDto {
  const factory LoginRequestDto({
    @JsonKey(name: 'identifier') required String identifier,
    @JsonKey(name: 'password') required String password,
  }) = _LoginRequestDto;

  factory LoginRequestDto.fromJson(Map<String, dynamic> json) =>
      _$LoginRequestDtoFromJson(json);
}
