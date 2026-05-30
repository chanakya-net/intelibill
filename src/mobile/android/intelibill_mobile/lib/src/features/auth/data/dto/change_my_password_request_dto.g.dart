// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'change_my_password_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ChangeMyPasswordRequestDto _$ChangeMyPasswordRequestDtoFromJson(
  Map<String, dynamic> json,
) => _ChangeMyPasswordRequestDto(
  currentPassword: json['currentPassword'] as String,
  newPassword: json['newPassword'] as String,
);

Map<String, dynamic> _$ChangeMyPasswordRequestDtoToJson(
  _ChangeMyPasswordRequestDto instance,
) => <String, dynamic>{
  'currentPassword': instance.currentPassword,
  'newPassword': instance.newPassword,
};
