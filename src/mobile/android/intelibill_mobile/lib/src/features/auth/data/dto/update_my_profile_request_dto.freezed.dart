// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'update_my_profile_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$UpdateMyProfileRequestDto {

@JsonKey(name: 'email') String get email;@JsonKey(name: 'phoneNumber') String? get phoneNumber;@JsonKey(name: 'firstName') String get firstName;@JsonKey(name: 'lastName') String get lastName;@JsonKey(name: 'language') String get language;
/// Create a copy of UpdateMyProfileRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$UpdateMyProfileRequestDtoCopyWith<UpdateMyProfileRequestDto> get copyWith => _$UpdateMyProfileRequestDtoCopyWithImpl<UpdateMyProfileRequestDto>(this as UpdateMyProfileRequestDto, _$identity);

  /// Serializes this UpdateMyProfileRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is UpdateMyProfileRequestDto&&(identical(other.email, email) || other.email == email)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.language, language) || other.language == language));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,email,phoneNumber,firstName,lastName,language);

@override
String toString() {
  return 'UpdateMyProfileRequestDto(email: $email, phoneNumber: $phoneNumber, firstName: $firstName, lastName: $lastName, language: $language)';
}


}

/// @nodoc
abstract mixin class $UpdateMyProfileRequestDtoCopyWith<$Res>  {
  factory $UpdateMyProfileRequestDtoCopyWith(UpdateMyProfileRequestDto value, $Res Function(UpdateMyProfileRequestDto) _then) = _$UpdateMyProfileRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'email') String email,@JsonKey(name: 'phoneNumber') String? phoneNumber,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'language') String language
});




}
/// @nodoc
class _$UpdateMyProfileRequestDtoCopyWithImpl<$Res>
    implements $UpdateMyProfileRequestDtoCopyWith<$Res> {
  _$UpdateMyProfileRequestDtoCopyWithImpl(this._self, this._then);

  final UpdateMyProfileRequestDto _self;
  final $Res Function(UpdateMyProfileRequestDto) _then;

/// Create a copy of UpdateMyProfileRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? email = null,Object? phoneNumber = freezed,Object? firstName = null,Object? lastName = null,Object? language = null,}) {
  return _then(_self.copyWith(
email: null == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: freezed == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String?,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,language: null == language ? _self.language : language // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [UpdateMyProfileRequestDto].
extension UpdateMyProfileRequestDtoPatterns on UpdateMyProfileRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _UpdateMyProfileRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _UpdateMyProfileRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _UpdateMyProfileRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _UpdateMyProfileRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _UpdateMyProfileRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _UpdateMyProfileRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'email')  String email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'language')  String language)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _UpdateMyProfileRequestDto() when $default != null:
return $default(_that.email,_that.phoneNumber,_that.firstName,_that.lastName,_that.language);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'email')  String email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'language')  String language)  $default,) {final _that = this;
switch (_that) {
case _UpdateMyProfileRequestDto():
return $default(_that.email,_that.phoneNumber,_that.firstName,_that.lastName,_that.language);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'email')  String email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'language')  String language)?  $default,) {final _that = this;
switch (_that) {
case _UpdateMyProfileRequestDto() when $default != null:
return $default(_that.email,_that.phoneNumber,_that.firstName,_that.lastName,_that.language);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _UpdateMyProfileRequestDto implements UpdateMyProfileRequestDto {
  const _UpdateMyProfileRequestDto({@JsonKey(name: 'email') required this.email, @JsonKey(name: 'phoneNumber') this.phoneNumber, @JsonKey(name: 'firstName') required this.firstName, @JsonKey(name: 'lastName') required this.lastName, @JsonKey(name: 'language') required this.language});
  factory _UpdateMyProfileRequestDto.fromJson(Map<String, dynamic> json) => _$UpdateMyProfileRequestDtoFromJson(json);

@override@JsonKey(name: 'email') final  String email;
@override@JsonKey(name: 'phoneNumber') final  String? phoneNumber;
@override@JsonKey(name: 'firstName') final  String firstName;
@override@JsonKey(name: 'lastName') final  String lastName;
@override@JsonKey(name: 'language') final  String language;

/// Create a copy of UpdateMyProfileRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$UpdateMyProfileRequestDtoCopyWith<_UpdateMyProfileRequestDto> get copyWith => __$UpdateMyProfileRequestDtoCopyWithImpl<_UpdateMyProfileRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$UpdateMyProfileRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _UpdateMyProfileRequestDto&&(identical(other.email, email) || other.email == email)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.language, language) || other.language == language));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,email,phoneNumber,firstName,lastName,language);

@override
String toString() {
  return 'UpdateMyProfileRequestDto(email: $email, phoneNumber: $phoneNumber, firstName: $firstName, lastName: $lastName, language: $language)';
}


}

/// @nodoc
abstract mixin class _$UpdateMyProfileRequestDtoCopyWith<$Res> implements $UpdateMyProfileRequestDtoCopyWith<$Res> {
  factory _$UpdateMyProfileRequestDtoCopyWith(_UpdateMyProfileRequestDto value, $Res Function(_UpdateMyProfileRequestDto) _then) = __$UpdateMyProfileRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'email') String email,@JsonKey(name: 'phoneNumber') String? phoneNumber,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'language') String language
});




}
/// @nodoc
class __$UpdateMyProfileRequestDtoCopyWithImpl<$Res>
    implements _$UpdateMyProfileRequestDtoCopyWith<$Res> {
  __$UpdateMyProfileRequestDtoCopyWithImpl(this._self, this._then);

  final _UpdateMyProfileRequestDto _self;
  final $Res Function(_UpdateMyProfileRequestDto) _then;

/// Create a copy of UpdateMyProfileRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? email = null,Object? phoneNumber = freezed,Object? firstName = null,Object? lastName = null,Object? language = null,}) {
  return _then(_UpdateMyProfileRequestDto(
email: null == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: freezed == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String?,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,language: null == language ? _self.language : language // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
