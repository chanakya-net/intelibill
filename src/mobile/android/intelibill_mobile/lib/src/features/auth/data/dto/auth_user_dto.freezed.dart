// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'auth_user_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AuthUserDto {

@JsonKey(name: 'id') String get id;@JsonKey(name: 'email') String? get email;@JsonKey(name: 'phoneNumber') String? get phoneNumber;@JsonKey(name: 'firstName') String get firstName;@JsonKey(name: 'lastName') String get lastName;@JsonKey(name: 'language') String? get language;
/// Create a copy of AuthUserDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AuthUserDtoCopyWith<AuthUserDto> get copyWith => _$AuthUserDtoCopyWithImpl<AuthUserDto>(this as AuthUserDto, _$identity);

  /// Serializes this AuthUserDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AuthUserDto&&(identical(other.id, id) || other.id == id)&&(identical(other.email, email) || other.email == email)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.language, language) || other.language == language));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,email,phoneNumber,firstName,lastName,language);

@override
String toString() {
  return 'AuthUserDto(id: $id, email: $email, phoneNumber: $phoneNumber, firstName: $firstName, lastName: $lastName, language: $language)';
}


}

/// @nodoc
abstract mixin class $AuthUserDtoCopyWith<$Res>  {
  factory $AuthUserDtoCopyWith(AuthUserDto value, $Res Function(AuthUserDto) _then) = _$AuthUserDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'email') String? email,@JsonKey(name: 'phoneNumber') String? phoneNumber,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'language') String? language
});




}
/// @nodoc
class _$AuthUserDtoCopyWithImpl<$Res>
    implements $AuthUserDtoCopyWith<$Res> {
  _$AuthUserDtoCopyWithImpl(this._self, this._then);

  final AuthUserDto _self;
  final $Res Function(AuthUserDto) _then;

/// Create a copy of AuthUserDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? email = freezed,Object? phoneNumber = freezed,Object? firstName = null,Object? lastName = null,Object? language = freezed,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,email: freezed == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String?,phoneNumber: freezed == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String?,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,language: freezed == language ? _self.language : language // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [AuthUserDto].
extension AuthUserDtoPatterns on AuthUserDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AuthUserDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AuthUserDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AuthUserDto value)  $default,){
final _that = this;
switch (_that) {
case _AuthUserDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AuthUserDto value)?  $default,){
final _that = this;
switch (_that) {
case _AuthUserDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'email')  String? email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'language')  String? language)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AuthUserDto() when $default != null:
return $default(_that.id,_that.email,_that.phoneNumber,_that.firstName,_that.lastName,_that.language);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'email')  String? email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'language')  String? language)  $default,) {final _that = this;
switch (_that) {
case _AuthUserDto():
return $default(_that.id,_that.email,_that.phoneNumber,_that.firstName,_that.lastName,_that.language);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'email')  String? email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'language')  String? language)?  $default,) {final _that = this;
switch (_that) {
case _AuthUserDto() when $default != null:
return $default(_that.id,_that.email,_that.phoneNumber,_that.firstName,_that.lastName,_that.language);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AuthUserDto implements AuthUserDto {
  const _AuthUserDto({@JsonKey(name: 'id') required this.id, @JsonKey(name: 'email') this.email, @JsonKey(name: 'phoneNumber') this.phoneNumber, @JsonKey(name: 'firstName') required this.firstName, @JsonKey(name: 'lastName') required this.lastName, @JsonKey(name: 'language') this.language});
  factory _AuthUserDto.fromJson(Map<String, dynamic> json) => _$AuthUserDtoFromJson(json);

@override@JsonKey(name: 'id') final  String id;
@override@JsonKey(name: 'email') final  String? email;
@override@JsonKey(name: 'phoneNumber') final  String? phoneNumber;
@override@JsonKey(name: 'firstName') final  String firstName;
@override@JsonKey(name: 'lastName') final  String lastName;
@override@JsonKey(name: 'language') final  String? language;

/// Create a copy of AuthUserDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AuthUserDtoCopyWith<_AuthUserDto> get copyWith => __$AuthUserDtoCopyWithImpl<_AuthUserDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AuthUserDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AuthUserDto&&(identical(other.id, id) || other.id == id)&&(identical(other.email, email) || other.email == email)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.language, language) || other.language == language));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,email,phoneNumber,firstName,lastName,language);

@override
String toString() {
  return 'AuthUserDto(id: $id, email: $email, phoneNumber: $phoneNumber, firstName: $firstName, lastName: $lastName, language: $language)';
}


}

/// @nodoc
abstract mixin class _$AuthUserDtoCopyWith<$Res> implements $AuthUserDtoCopyWith<$Res> {
  factory _$AuthUserDtoCopyWith(_AuthUserDto value, $Res Function(_AuthUserDto) _then) = __$AuthUserDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'email') String? email,@JsonKey(name: 'phoneNumber') String? phoneNumber,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'language') String? language
});




}
/// @nodoc
class __$AuthUserDtoCopyWithImpl<$Res>
    implements _$AuthUserDtoCopyWith<$Res> {
  __$AuthUserDtoCopyWithImpl(this._self, this._then);

  final _AuthUserDto _self;
  final $Res Function(_AuthUserDto) _then;

/// Create a copy of AuthUserDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? email = freezed,Object? phoneNumber = freezed,Object? firstName = null,Object? lastName = null,Object? language = freezed,}) {
  return _then(_AuthUserDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,email: freezed == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String?,phoneNumber: freezed == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String?,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,language: freezed == language ? _self.language : language // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
