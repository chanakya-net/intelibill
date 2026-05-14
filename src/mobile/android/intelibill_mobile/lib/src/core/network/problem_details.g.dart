// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'problem_details.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ProblemDetails _$ProblemDetailsFromJson(Map<String, dynamic> json) =>
    _ProblemDetails(
      type: json['type'] as String?,
      title: json['title'] as String?,
      status: (json['status'] as num?)?.toInt(),
      detail: json['detail'] as String?,
      instance: json['instance'] as String?,
      errors: json['errors'] == null
          ? const {}
          : _errorsFromJson(json['errors']),
    );

Map<String, dynamic> _$ProblemDetailsToJson(_ProblemDetails instance) =>
    <String, dynamic>{
      'type': instance.type,
      'title': instance.title,
      'status': instance.status,
      'detail': instance.detail,
      'instance': instance.instance,
      'errors': instance.errors,
    };
