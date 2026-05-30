// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'app_status_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AppStatusDto {

@JsonKey(name: 'statusText') String get statusText;@JsonKey(name: 'apiBaseUrl') String get apiBaseUrl;@JsonKey(name: 'timestamp') DateTime get timestamp;@JsonKey(name: 'environment') String? get environment;
/// Create a copy of AppStatusDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AppStatusDtoCopyWith<AppStatusDto> get copyWith => _$AppStatusDtoCopyWithImpl<AppStatusDto>(this as AppStatusDto, _$identity);

  /// Serializes this AppStatusDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AppStatusDto&&(identical(other.statusText, statusText) || other.statusText == statusText)&&(identical(other.apiBaseUrl, apiBaseUrl) || other.apiBaseUrl == apiBaseUrl)&&(identical(other.timestamp, timestamp) || other.timestamp == timestamp)&&(identical(other.environment, environment) || other.environment == environment));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,statusText,apiBaseUrl,timestamp,environment);

@override
String toString() {
  return 'AppStatusDto(statusText: $statusText, apiBaseUrl: $apiBaseUrl, timestamp: $timestamp, environment: $environment)';
}


}

/// @nodoc
abstract mixin class $AppStatusDtoCopyWith<$Res>  {
  factory $AppStatusDtoCopyWith(AppStatusDto value, $Res Function(AppStatusDto) _then) = _$AppStatusDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'statusText') String statusText,@JsonKey(name: 'apiBaseUrl') String apiBaseUrl,@JsonKey(name: 'timestamp') DateTime timestamp,@JsonKey(name: 'environment') String? environment
});




}
/// @nodoc
class _$AppStatusDtoCopyWithImpl<$Res>
    implements $AppStatusDtoCopyWith<$Res> {
  _$AppStatusDtoCopyWithImpl(this._self, this._then);

  final AppStatusDto _self;
  final $Res Function(AppStatusDto) _then;

/// Create a copy of AppStatusDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? statusText = null,Object? apiBaseUrl = null,Object? timestamp = null,Object? environment = freezed,}) {
  return _then(_self.copyWith(
statusText: null == statusText ? _self.statusText : statusText // ignore: cast_nullable_to_non_nullable
as String,apiBaseUrl: null == apiBaseUrl ? _self.apiBaseUrl : apiBaseUrl // ignore: cast_nullable_to_non_nullable
as String,timestamp: null == timestamp ? _self.timestamp : timestamp // ignore: cast_nullable_to_non_nullable
as DateTime,environment: freezed == environment ? _self.environment : environment // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [AppStatusDto].
extension AppStatusDtoPatterns on AppStatusDto {
/// A variant of `map` that fallback to returning `orElse`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AppStatusDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AppStatusDto() when $default != null:
return $default(_that);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// Callbacks receives the raw object, upcasted.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case final Subclass2 value:
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AppStatusDto value)  $default,){
final _that = this;
switch (_that) {
case _AppStatusDto():
return $default(_that);}
}
/// A variant of `map` that fallback to returning `null`.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case final Subclass value:
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AppStatusDto value)?  $default,){
final _that = this;
switch (_that) {
case _AppStatusDto() when $default != null:
return $default(_that);case _:
  return null;

}
}
/// A variant of `when` that fallback to an `orElse` callback.
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return orElse();
/// }
/// ```

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'statusText')  String statusText, @JsonKey(name: 'apiBaseUrl')  String apiBaseUrl, @JsonKey(name: 'timestamp')  DateTime timestamp, @JsonKey(name: 'environment')  String? environment)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AppStatusDto() when $default != null:
return $default(_that.statusText,_that.apiBaseUrl,_that.timestamp,_that.environment);case _:
  return orElse();

}
}
/// A `switch`-like method, using callbacks.
///
/// As opposed to `map`, this offers destructuring.
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case Subclass2(:final field2):
///     return ...;
/// }
/// ```

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'statusText')  String statusText, @JsonKey(name: 'apiBaseUrl')  String apiBaseUrl, @JsonKey(name: 'timestamp')  DateTime timestamp, @JsonKey(name: 'environment')  String? environment)  $default,) {final _that = this;
switch (_that) {
case _AppStatusDto():
return $default(_that.statusText,_that.apiBaseUrl,_that.timestamp,_that.environment);}
}
/// A variant of `when` that fallback to returning `null`
///
/// It is equivalent to doing:
/// ```dart
/// switch (sealedClass) {
///   case Subclass(:final field):
///     return ...;
///   case _:
///     return null;
/// }
/// ```

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'statusText')  String statusText, @JsonKey(name: 'apiBaseUrl')  String apiBaseUrl, @JsonKey(name: 'timestamp')  DateTime timestamp, @JsonKey(name: 'environment')  String? environment)?  $default,) {final _that = this;
switch (_that) {
case _AppStatusDto() when $default != null:
return $default(_that.statusText,_that.apiBaseUrl,_that.timestamp,_that.environment);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AppStatusDto implements AppStatusDto {
  const _AppStatusDto({@JsonKey(name: 'statusText') required this.statusText, @JsonKey(name: 'apiBaseUrl') required this.apiBaseUrl, @JsonKey(name: 'timestamp') required this.timestamp, @JsonKey(name: 'environment') this.environment});
  factory _AppStatusDto.fromJson(Map<String, dynamic> json) => _$AppStatusDtoFromJson(json);

@override@JsonKey(name: 'statusText') final  String statusText;
@override@JsonKey(name: 'apiBaseUrl') final  String apiBaseUrl;
@override@JsonKey(name: 'timestamp') final  DateTime timestamp;
@override@JsonKey(name: 'environment') final  String? environment;

/// Create a copy of AppStatusDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AppStatusDtoCopyWith<_AppStatusDto> get copyWith => __$AppStatusDtoCopyWithImpl<_AppStatusDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AppStatusDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AppStatusDto&&(identical(other.statusText, statusText) || other.statusText == statusText)&&(identical(other.apiBaseUrl, apiBaseUrl) || other.apiBaseUrl == apiBaseUrl)&&(identical(other.timestamp, timestamp) || other.timestamp == timestamp)&&(identical(other.environment, environment) || other.environment == environment));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,statusText,apiBaseUrl,timestamp,environment);

@override
String toString() {
  return 'AppStatusDto(statusText: $statusText, apiBaseUrl: $apiBaseUrl, timestamp: $timestamp, environment: $environment)';
}


}

/// @nodoc
abstract mixin class _$AppStatusDtoCopyWith<$Res> implements $AppStatusDtoCopyWith<$Res> {
  factory _$AppStatusDtoCopyWith(_AppStatusDto value, $Res Function(_AppStatusDto) _then) = __$AppStatusDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'statusText') String statusText,@JsonKey(name: 'apiBaseUrl') String apiBaseUrl,@JsonKey(name: 'timestamp') DateTime timestamp,@JsonKey(name: 'environment') String? environment
});




}
/// @nodoc
class __$AppStatusDtoCopyWithImpl<$Res>
    implements _$AppStatusDtoCopyWith<$Res> {
  __$AppStatusDtoCopyWithImpl(this._self, this._then);

  final _AppStatusDto _self;
  final $Res Function(_AppStatusDto) _then;

/// Create a copy of AppStatusDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? statusText = null,Object? apiBaseUrl = null,Object? timestamp = null,Object? environment = freezed,}) {
  return _then(_AppStatusDto(
statusText: null == statusText ? _self.statusText : statusText // ignore: cast_nullable_to_non_nullable
as String,apiBaseUrl: null == apiBaseUrl ? _self.apiBaseUrl : apiBaseUrl // ignore: cast_nullable_to_non_nullable
as String,timestamp: null == timestamp ? _self.timestamp : timestamp // ignore: cast_nullable_to_non_nullable
as DateTime,environment: freezed == environment ? _self.environment : environment // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
