import 'package:freezed_annotation/freezed_annotation.dart';

part 'change_my_password_request_dto.freezed.dart';
part 'change_my_password_request_dto.g.dart';

@freezed
sealed class ChangeMyPasswordRequestDto with _$ChangeMyPasswordRequestDto {
  const factory ChangeMyPasswordRequestDto({
    @JsonKey(name: 'currentPassword') required String currentPassword,
    @JsonKey(name: 'newPassword') required String newPassword,
  }) = _ChangeMyPasswordRequestDto;

  factory ChangeMyPasswordRequestDto.fromJson(Map<String, dynamic> json) =>
      _$ChangeMyPasswordRequestDtoFromJson(json);
}
