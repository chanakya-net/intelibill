// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'edit_shop_user_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$EditShopUserRequestDto {

@JsonKey(name: 'email') String get email;@JsonKey(name: 'firstName') String get firstName;@JsonKey(name: 'lastName') String get lastName;@JsonKey(name: 'phoneNumber') String get phoneNumber;@JsonKey(name: 'role') String get role;@JsonKey(name: 'isLoginEnabled') bool get isLoginEnabled;@JsonKey(name: 'shopIds') List<String> get shopIds;
/// Create a copy of EditShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$EditShopUserRequestDtoCopyWith<EditShopUserRequestDto> get copyWith => _$EditShopUserRequestDtoCopyWithImpl<EditShopUserRequestDto>(this as EditShopUserRequestDto, _$identity);

  /// Serializes this EditShopUserRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is EditShopUserRequestDto&&(identical(other.email, email) || other.email == email)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.role, role) || other.role == role)&&(identical(other.isLoginEnabled, isLoginEnabled) || other.isLoginEnabled == isLoginEnabled)&&const DeepCollectionEquality().equals(other.shopIds, shopIds));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,email,firstName,lastName,phoneNumber,role,isLoginEnabled,const DeepCollectionEquality().hash(shopIds));

@override
String toString() {
  return 'EditShopUserRequestDto(email: $email, firstName: $firstName, lastName: $lastName, phoneNumber: $phoneNumber, role: $role, isLoginEnabled: $isLoginEnabled, shopIds: $shopIds)';
}


}

/// @nodoc
abstract mixin class $EditShopUserRequestDtoCopyWith<$Res>  {
  factory $EditShopUserRequestDtoCopyWith(EditShopUserRequestDto value, $Res Function(EditShopUserRequestDto) _then) = _$EditShopUserRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'email') String email,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'role') String role,@JsonKey(name: 'isLoginEnabled') bool isLoginEnabled,@JsonKey(name: 'shopIds') List<String> shopIds
});




}
/// @nodoc
class _$EditShopUserRequestDtoCopyWithImpl<$Res>
    implements $EditShopUserRequestDtoCopyWith<$Res> {
  _$EditShopUserRequestDtoCopyWithImpl(this._self, this._then);

  final EditShopUserRequestDto _self;
  final $Res Function(EditShopUserRequestDto) _then;

/// Create a copy of EditShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? email = null,Object? firstName = null,Object? lastName = null,Object? phoneNumber = null,Object? role = null,Object? isLoginEnabled = null,Object? shopIds = null,}) {
  return _then(_self.copyWith(
email: null == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,isLoginEnabled: null == isLoginEnabled ? _self.isLoginEnabled : isLoginEnabled // ignore: cast_nullable_to_non_nullable
as bool,shopIds: null == shopIds ? _self.shopIds : shopIds // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}

}


/// Adds pattern-matching-related methods to [EditShopUserRequestDto].
extension EditShopUserRequestDtoPatterns on EditShopUserRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _EditShopUserRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _EditShopUserRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _EditShopUserRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _EditShopUserRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _EditShopUserRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _EditShopUserRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'email')  String email, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isLoginEnabled')  bool isLoginEnabled, @JsonKey(name: 'shopIds')  List<String> shopIds)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _EditShopUserRequestDto() when $default != null:
return $default(_that.email,_that.firstName,_that.lastName,_that.phoneNumber,_that.role,_that.isLoginEnabled,_that.shopIds);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'email')  String email, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isLoginEnabled')  bool isLoginEnabled, @JsonKey(name: 'shopIds')  List<String> shopIds)  $default,) {final _that = this;
switch (_that) {
case _EditShopUserRequestDto():
return $default(_that.email,_that.firstName,_that.lastName,_that.phoneNumber,_that.role,_that.isLoginEnabled,_that.shopIds);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'email')  String email, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isLoginEnabled')  bool isLoginEnabled, @JsonKey(name: 'shopIds')  List<String> shopIds)?  $default,) {final _that = this;
switch (_that) {
case _EditShopUserRequestDto() when $default != null:
return $default(_that.email,_that.firstName,_that.lastName,_that.phoneNumber,_that.role,_that.isLoginEnabled,_that.shopIds);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _EditShopUserRequestDto implements EditShopUserRequestDto {
  const _EditShopUserRequestDto({@JsonKey(name: 'email') required this.email, @JsonKey(name: 'firstName') required this.firstName, @JsonKey(name: 'lastName') required this.lastName, @JsonKey(name: 'phoneNumber') required this.phoneNumber, @JsonKey(name: 'role') required this.role, @JsonKey(name: 'isLoginEnabled') required this.isLoginEnabled, @JsonKey(name: 'shopIds') required final  List<String> shopIds}): _shopIds = shopIds;
  factory _EditShopUserRequestDto.fromJson(Map<String, dynamic> json) => _$EditShopUserRequestDtoFromJson(json);

@override@JsonKey(name: 'email') final  String email;
@override@JsonKey(name: 'firstName') final  String firstName;
@override@JsonKey(name: 'lastName') final  String lastName;
@override@JsonKey(name: 'phoneNumber') final  String phoneNumber;
@override@JsonKey(name: 'role') final  String role;
@override@JsonKey(name: 'isLoginEnabled') final  bool isLoginEnabled;
 final  List<String> _shopIds;
@override@JsonKey(name: 'shopIds') List<String> get shopIds {
  if (_shopIds is EqualUnmodifiableListView) return _shopIds;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_shopIds);
}


/// Create a copy of EditShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$EditShopUserRequestDtoCopyWith<_EditShopUserRequestDto> get copyWith => __$EditShopUserRequestDtoCopyWithImpl<_EditShopUserRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$EditShopUserRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _EditShopUserRequestDto&&(identical(other.email, email) || other.email == email)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.role, role) || other.role == role)&&(identical(other.isLoginEnabled, isLoginEnabled) || other.isLoginEnabled == isLoginEnabled)&&const DeepCollectionEquality().equals(other._shopIds, _shopIds));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,email,firstName,lastName,phoneNumber,role,isLoginEnabled,const DeepCollectionEquality().hash(_shopIds));

@override
String toString() {
  return 'EditShopUserRequestDto(email: $email, firstName: $firstName, lastName: $lastName, phoneNumber: $phoneNumber, role: $role, isLoginEnabled: $isLoginEnabled, shopIds: $shopIds)';
}


}

/// @nodoc
abstract mixin class _$EditShopUserRequestDtoCopyWith<$Res> implements $EditShopUserRequestDtoCopyWith<$Res> {
  factory _$EditShopUserRequestDtoCopyWith(_EditShopUserRequestDto value, $Res Function(_EditShopUserRequestDto) _then) = __$EditShopUserRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'email') String email,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'role') String role,@JsonKey(name: 'isLoginEnabled') bool isLoginEnabled,@JsonKey(name: 'shopIds') List<String> shopIds
});




}
/// @nodoc
class __$EditShopUserRequestDtoCopyWithImpl<$Res>
    implements _$EditShopUserRequestDtoCopyWith<$Res> {
  __$EditShopUserRequestDtoCopyWithImpl(this._self, this._then);

  final _EditShopUserRequestDto _self;
  final $Res Function(_EditShopUserRequestDto) _then;

/// Create a copy of EditShopUserRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? email = null,Object? firstName = null,Object? lastName = null,Object? phoneNumber = null,Object? role = null,Object? isLoginEnabled = null,Object? shopIds = null,}) {
  return _then(_EditShopUserRequestDto(
email: null == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,isLoginEnabled: null == isLoginEnabled ? _self.isLoginEnabled : isLoginEnabled // ignore: cast_nullable_to_non_nullable
as bool,shopIds: null == shopIds ? _self._shopIds : shopIds // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}


}

// dart format on
