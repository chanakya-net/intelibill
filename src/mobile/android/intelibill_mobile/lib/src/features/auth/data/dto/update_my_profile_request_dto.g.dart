// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'update_my_profile_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_UpdateMyProfileRequestDto _$UpdateMyProfileRequestDtoFromJson(
  Map<String, dynamic> json,
) => _UpdateMyProfileRequestDto(
  email: json['email'] as String,
  phoneNumber: json['phoneNumber'] as String?,
  firstName: json['firstName'] as String,
  lastName: json['lastName'] as String,
  language: json['language'] as String,
);

Map<String, dynamic> _$UpdateMyProfileRequestDtoToJson(
  _UpdateMyProfileRequestDto instance,
) => <String, dynamic>{
  'email': instance.email,
  'phoneNumber': instance.phoneNumber,
  'firstName': instance.firstName,
  'lastName': instance.lastName,
  'language': instance.language,
};
