// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'change_my_password_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ChangeMyPasswordRequestDto {

@JsonKey(name: 'currentPassword') String get currentPassword;@JsonKey(name: 'newPassword') String get newPassword;
/// Create a copy of ChangeMyPasswordRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ChangeMyPasswordRequestDtoCopyWith<ChangeMyPasswordRequestDto> get copyWith => _$ChangeMyPasswordRequestDtoCopyWithImpl<ChangeMyPasswordRequestDto>(this as ChangeMyPasswordRequestDto, _$identity);

  /// Serializes this ChangeMyPasswordRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ChangeMyPasswordRequestDto&&(identical(other.currentPassword, currentPassword) || other.currentPassword == currentPassword)&&(identical(other.newPassword, newPassword) || other.newPassword == newPassword));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,currentPassword,newPassword);

@override
String toString() {
  return 'ChangeMyPasswordRequestDto(currentPassword: $currentPassword, newPassword: $newPassword)';
}


}

/// @nodoc
abstract mixin class $ChangeMyPasswordRequestDtoCopyWith<$Res>  {
  factory $ChangeMyPasswordRequestDtoCopyWith(ChangeMyPasswordRequestDto value, $Res Function(ChangeMyPasswordRequestDto) _then) = _$ChangeMyPasswordRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'currentPassword') String currentPassword,@JsonKey(name: 'newPassword') String newPassword
});




}
/// @nodoc
class _$ChangeMyPasswordRequestDtoCopyWithImpl<$Res>
    implements $ChangeMyPasswordRequestDtoCopyWith<$Res> {
  _$ChangeMyPasswordRequestDtoCopyWithImpl(this._self, this._then);

  final ChangeMyPasswordRequestDto _self;
  final $Res Function(ChangeMyPasswordRequestDto) _then;

/// Create a copy of ChangeMyPasswordRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? currentPassword = null,Object? newPassword = null,}) {
  return _then(_self.copyWith(
currentPassword: null == currentPassword ? _self.currentPassword : currentPassword // ignore: cast_nullable_to_non_nullable
as String,newPassword: null == newPassword ? _self.newPassword : newPassword // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [ChangeMyPasswordRequestDto].
extension ChangeMyPasswordRequestDtoPatterns on ChangeMyPasswordRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ChangeMyPasswordRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ChangeMyPasswordRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ChangeMyPasswordRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _ChangeMyPasswordRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ChangeMyPasswordRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _ChangeMyPasswordRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'currentPassword')  String currentPassword, @JsonKey(name: 'newPassword')  String newPassword)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ChangeMyPasswordRequestDto() when $default != null:
return $default(_that.currentPassword,_that.newPassword);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'currentPassword')  String currentPassword, @JsonKey(name: 'newPassword')  String newPassword)  $default,) {final _that = this;
switch (_that) {
case _ChangeMyPasswordRequestDto():
return $default(_that.currentPassword,_that.newPassword);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'currentPassword')  String currentPassword, @JsonKey(name: 'newPassword')  String newPassword)?  $default,) {final _that = this;
switch (_that) {
case _ChangeMyPasswordRequestDto() when $default != null:
return $default(_that.currentPassword,_that.newPassword);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ChangeMyPasswordRequestDto implements ChangeMyPasswordRequestDto {
  const _ChangeMyPasswordRequestDto({@JsonKey(name: 'currentPassword') required this.currentPassword, @JsonKey(name: 'newPassword') required this.newPassword});
  factory _ChangeMyPasswordRequestDto.fromJson(Map<String, dynamic> json) => _$ChangeMyPasswordRequestDtoFromJson(json);

@override@JsonKey(name: 'currentPassword') final  String currentPassword;
@override@JsonKey(name: 'newPassword') final  String newPassword;

/// Create a copy of ChangeMyPasswordRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ChangeMyPasswordRequestDtoCopyWith<_ChangeMyPasswordRequestDto> get copyWith => __$ChangeMyPasswordRequestDtoCopyWithImpl<_ChangeMyPasswordRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ChangeMyPasswordRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ChangeMyPasswordRequestDto&&(identical(other.currentPassword, currentPassword) || other.currentPassword == currentPassword)&&(identical(other.newPassword, newPassword) || other.newPassword == newPassword));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,currentPassword,newPassword);

@override
String toString() {
  return 'ChangeMyPasswordRequestDto(currentPassword: $currentPassword, newPassword: $newPassword)';
}


}

/// @nodoc
abstract mixin class _$ChangeMyPasswordRequestDtoCopyWith<$Res> implements $ChangeMyPasswordRequestDtoCopyWith<$Res> {
  factory _$ChangeMyPasswordRequestDtoCopyWith(_ChangeMyPasswordRequestDto value, $Res Function(_ChangeMyPasswordRequestDto) _then) = __$ChangeMyPasswordRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'currentPassword') String currentPassword,@JsonKey(name: 'newPassword') String newPassword
});




}
/// @nodoc
class __$ChangeMyPasswordRequestDtoCopyWithImpl<$Res>
    implements _$ChangeMyPasswordRequestDtoCopyWith<$Res> {
  __$ChangeMyPasswordRequestDtoCopyWithImpl(this._self, this._then);

  final _ChangeMyPasswordRequestDto _self;
  final $Res Function(_ChangeMyPasswordRequestDto) _then;

/// Create a copy of ChangeMyPasswordRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? currentPassword = null,Object? newPassword = null,}) {
  return _then(_ChangeMyPasswordRequestDto(
currentPassword: null == currentPassword ? _self.currentPassword : currentPassword // ignore: cast_nullable_to_non_nullable
as String,newPassword: null == newPassword ? _self.newPassword : newPassword // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
