import 'package:freezed_annotation/freezed_annotation.dart';

part 'update_my_profile_request_dto.freezed.dart';
part 'update_my_profile_request_dto.g.dart';

@freezed
sealed class UpdateMyProfileRequestDto with _$UpdateMyProfileRequestDto {
  const factory UpdateMyProfileRequestDto({
    @JsonKey(name: 'email') required String email,
    @JsonKey(name: 'phoneNumber') String? phoneNumber,
    @JsonKey(name: 'firstName') required String firstName,
    @JsonKey(name: 'lastName') required String lastName,
    @JsonKey(name: 'language') required String language,
  }) = _UpdateMyProfileRequestDto;

  factory UpdateMyProfileRequestDto.fromJson(Map<String, dynamic> json) =>
      _$UpdateMyProfileRequestDtoFromJson(json);
}
