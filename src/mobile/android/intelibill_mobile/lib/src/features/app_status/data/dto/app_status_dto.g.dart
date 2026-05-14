// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'app_status_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AppStatusDto _$AppStatusDtoFromJson(Map<String, dynamic> json) =>
    _AppStatusDto(
      statusText: json['statusText'] as String,
      apiBaseUrl: json['apiBaseUrl'] as String,
      timestamp: DateTime.parse(json['timestamp'] as String),
      environment: json['environment'] as String?,
    );

Map<String, dynamic> _$AppStatusDtoToJson(_AppStatusDto instance) =>
    <String, dynamic>{
      'statusText': instance.statusText,
      'apiBaseUrl': instance.apiBaseUrl,
      'timestamp': instance.timestamp.toIso8601String(),
      'environment': instance.environment,
    };
