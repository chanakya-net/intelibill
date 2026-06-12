// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'add_shop_user_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AddShopUserRequestDto {

@JsonKey(name: 'shopIds') List<String> get shopIds;@JsonKey(name: 'email') String get email;@JsonKey(name: 'firstName') String get firstName;@JsonKey(name: 'lastName') String get lastName;@JsonKey(name: 'phoneNumber') String get phoneNumber;@JsonKey(name: 'password') String get password;@JsonKey(name: 'confirmPassword') String get confirmPassword;@JsonKey(name: 'role') String get role;
/// Create a copy of AddShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddShopUserRequestDtoCopyWith<AddShopUserRequestDto> get copyWith => _$AddShopUserRequestDtoCopyWithImpl<AddShopUserRequestDto>(this as AddShopUserRequestDto, _$identity);

  /// Serializes this AddShopUserRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddShopUserRequestDto&&const DeepCollectionEquality().equals(other.shopIds, shopIds)&&(identical(other.email, email) || other.email == email)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.password, password) || other.password == password)&&(identical(other.confirmPassword, confirmPassword) || other.confirmPassword == confirmPassword)&&(identical(other.role, role) || other.role == role));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(shopIds),email,firstName,lastName,phoneNumber,password,confirmPassword,role);

@override
String toString() {
  return 'AddShopUserRequestDto(shopIds: $shopIds, email: $email, firstName: $firstName, lastName: $lastName, phoneNumber: $phoneNumber, password: $password, confirmPassword: $confirmPassword, role: $role)';
}


}

/// @nodoc
abstract mixin class $AddShopUserRequestDtoCopyWith<$Res>  {
  factory $AddShopUserRequestDtoCopyWith(AddShopUserRequestDto value, $Res Function(AddShopUserRequestDto) _then) = _$AddShopUserRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'shopIds') List<String> shopIds,@JsonKey(name: 'email') String email,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'password') String password,@JsonKey(name: 'confirmPassword') String confirmPassword,@JsonKey(name: 'role') String role
});




}
/// @nodoc
class _$AddShopUserRequestDtoCopyWithImpl<$Res>
    implements $AddShopUserRequestDtoCopyWith<$Res> {
  _$AddShopUserRequestDtoCopyWithImpl(this._self, this._then);

  final AddShopUserRequestDto _self;
  final $Res Function(AddShopUserRequestDto) _then;

/// Create a copy of AddShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? shopIds = null,Object? email = null,Object? firstName = null,Object? lastName = null,Object? phoneNumber = null,Object? password = null,Object? confirmPassword = null,Object? role = null,}) {
  return _then(_self.copyWith(
shopIds: null == shopIds ? _self.shopIds : shopIds // ignore: cast_nullable_to_non_nullable
as List<String>,email: null == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,password: null == password ? _self.password : password // ignore: cast_nullable_to_non_nullable
as String,confirmPassword: null == confirmPassword ? _self.confirmPassword : confirmPassword // ignore: cast_nullable_to_non_nullable
as String,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [AddShopUserRequestDto].
extension AddShopUserRequestDtoPatterns on AddShopUserRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddShopUserRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddShopUserRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddShopUserRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _AddShopUserRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddShopUserRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddShopUserRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopIds')  List<String> shopIds, @JsonKey(name: 'email')  String email, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'password')  String password, @JsonKey(name: 'confirmPassword')  String confirmPassword, @JsonKey(name: 'role')  String role)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddShopUserRequestDto() when $default != null:
return $default(_that.shopIds,_that.email,_that.firstName,_that.lastName,_that.phoneNumber,_that.password,_that.confirmPassword,_that.role);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopIds')  List<String> shopIds, @JsonKey(name: 'email')  String email, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'password')  String password, @JsonKey(name: 'confirmPassword')  String confirmPassword, @JsonKey(name: 'role')  String role)  $default,) {final _that = this;
switch (_that) {
case _AddShopUserRequestDto():
return $default(_that.shopIds,_that.email,_that.firstName,_that.lastName,_that.phoneNumber,_that.password,_that.confirmPassword,_that.role);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'shopIds')  List<String> shopIds, @JsonKey(name: 'email')  String email, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'password')  String password, @JsonKey(name: 'confirmPassword')  String confirmPassword, @JsonKey(name: 'role')  String role)?  $default,) {final _that = this;
switch (_that) {
case _AddShopUserRequestDto() when $default != null:
return $default(_that.shopIds,_that.email,_that.firstName,_that.lastName,_that.phoneNumber,_that.password,_that.confirmPassword,_that.role);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddShopUserRequestDto implements AddShopUserRequestDto {
  const _AddShopUserRequestDto({@JsonKey(name: 'shopIds') required final  List<String> shopIds, @JsonKey(name: 'email') required this.email, @JsonKey(name: 'firstName') required this.firstName, @JsonKey(name: 'lastName') required this.lastName, @JsonKey(name: 'phoneNumber') required this.phoneNumber, @JsonKey(name: 'password') required this.password, @JsonKey(name: 'confirmPassword') required this.confirmPassword, @JsonKey(name: 'role') required this.role}): _shopIds = shopIds;
  factory _AddShopUserRequestDto.fromJson(Map<String, dynamic> json) => _$AddShopUserRequestDtoFromJson(json);

 final  List<String> _shopIds;
@override@JsonKey(name: 'shopIds') List<String> get shopIds {
  if (_shopIds is EqualUnmodifiableListView) return _shopIds;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_shopIds);
}

@override@JsonKey(name: 'email') final  String email;
@override@JsonKey(name: 'firstName') final  String firstName;
@override@JsonKey(name: 'lastName') final  String lastName;
@override@JsonKey(name: 'phoneNumber') final  String phoneNumber;
@override@JsonKey(name: 'password') final  String password;
@override@JsonKey(name: 'confirmPassword') final  String confirmPassword;
@override@JsonKey(name: 'role') final  String role;

/// Create a copy of AddShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddShopUserRequestDtoCopyWith<_AddShopUserRequestDto> get copyWith => __$AddShopUserRequestDtoCopyWithImpl<_AddShopUserRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddShopUserRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddShopUserRequestDto&&const DeepCollectionEquality().equals(other._shopIds, _shopIds)&&(identical(other.email, email) || other.email == email)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.password, password) || other.password == password)&&(identical(other.confirmPassword, confirmPassword) || other.confirmPassword == confirmPassword)&&(identical(other.role, role) || other.role == role));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_shopIds),email,firstName,lastName,phoneNumber,password,confirmPassword,role);

@override
String toString() {
  return 'AddShopUserRequestDto(shopIds: $shopIds, email: $email, firstName: $firstName, lastName: $lastName, phoneNumber: $phoneNumber, password: $password, confirmPassword: $confirmPassword, role: $role)';
}


}

/// @nodoc
abstract mixin class _$AddShopUserRequestDtoCopyWith<$Res> implements $AddShopUserRequestDtoCopyWith<$Res> {
  factory _$AddShopUserRequestDtoCopyWith(_AddShopUserRequestDto value, $Res Function(_AddShopUserRequestDto) _then) = __$AddShopUserRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'shopIds') List<String> shopIds,@JsonKey(name: 'email') String email,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'password') String password,@JsonKey(name: 'confirmPassword') String confirmPassword,@JsonKey(name: 'role') String role
});




}
/// @nodoc
class __$AddShopUserRequestDtoCopyWithImpl<$Res>
    implements _$AddShopUserRequestDtoCopyWith<$Res> {
  __$AddShopUserRequestDtoCopyWithImpl(this._self, this._then);

  final _AddShopUserRequestDto _self;
  final $Res Function(_AddShopUserRequestDto) _then;

/// Create a copy of AddShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? shopIds = null,Object? email = null,Object? firstName = null,Object? lastName = null,Object? phoneNumber = null,Object? password = null,Object? confirmPassword = null,Object? role = null,}) {
  return _then(_AddShopUserRequestDto(
shopIds: null == shopIds ? _self._shopIds : shopIds // ignore: cast_nullable_to_non_nullable
as List<String>,email: null == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,password: null == password ? _self.password : password // ignore: cast_nullable_to_non_nullable
as String,confirmPassword: null == confirmPassword ? _self.confirmPassword : confirmPassword // ignore: cast_nullable_to_non_nullable
as String,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
