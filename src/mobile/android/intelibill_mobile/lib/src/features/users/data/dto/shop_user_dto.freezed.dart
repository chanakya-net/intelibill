// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'shop_user_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ShopUserDto {

@JsonKey(name: 'userId') String get userId;@JsonKey(name: 'firstName') String get firstName;@JsonKey(name: 'lastName') String get lastName;@JsonKey(name: 'email') String? get email;@JsonKey(name: 'phoneNumber') String? get phoneNumber;@JsonKey(name: 'role') String get role;@JsonKey(name: 'isLoginEnabled') bool get isLoginEnabled;@JsonKey(name: 'shopIds') List<String> get shopIds;
/// Create a copy of ShopUserDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ShopUserDtoCopyWith<ShopUserDto> get copyWith => _$ShopUserDtoCopyWithImpl<ShopUserDto>(this as ShopUserDto, _$identity);

  /// Serializes this ShopUserDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ShopUserDto&&(identical(other.userId, userId) || other.userId == userId)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.email, email) || other.email == email)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.role, role) || other.role == role)&&(identical(other.isLoginEnabled, isLoginEnabled) || other.isLoginEnabled == isLoginEnabled)&&const DeepCollectionEquality().equals(other.shopIds, shopIds));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,userId,firstName,lastName,email,phoneNumber,role,isLoginEnabled,const DeepCollectionEquality().hash(shopIds));

@override
String toString() {
  return 'ShopUserDto(userId: $userId, firstName: $firstName, lastName: $lastName, email: $email, phoneNumber: $phoneNumber, role: $role, isLoginEnabled: $isLoginEnabled, shopIds: $shopIds)';
}


}

/// @nodoc
abstract mixin class $ShopUserDtoCopyWith<$Res>  {
  factory $ShopUserDtoCopyWith(ShopUserDto value, $Res Function(ShopUserDto) _then) = _$ShopUserDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'userId') String userId,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'email') String? email,@JsonKey(name: 'phoneNumber') String? phoneNumber,@JsonKey(name: 'role') String role,@JsonKey(name: 'isLoginEnabled') bool isLoginEnabled,@JsonKey(name: 'shopIds') List<String> shopIds
});




}
/// @nodoc
class _$ShopUserDtoCopyWithImpl<$Res>
    implements $ShopUserDtoCopyWith<$Res> {
  _$ShopUserDtoCopyWithImpl(this._self, this._then);

  final ShopUserDto _self;
  final $Res Function(ShopUserDto) _then;

/// Create a copy of ShopUserDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? userId = null,Object? firstName = null,Object? lastName = null,Object? email = freezed,Object? phoneNumber = freezed,Object? role = null,Object? isLoginEnabled = null,Object? shopIds = null,}) {
  return _then(_self.copyWith(
userId: null == userId ? _self.userId : userId // ignore: cast_nullable_to_non_nullable
as String,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,email: freezed == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String?,phoneNumber: freezed == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String?,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,isLoginEnabled: null == isLoginEnabled ? _self.isLoginEnabled : isLoginEnabled // ignore: cast_nullable_to_non_nullable
as bool,shopIds: null == shopIds ? _self.shopIds : shopIds // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}

}


/// Adds pattern-matching-related methods to [ShopUserDto].
extension ShopUserDtoPatterns on ShopUserDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ShopUserDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ShopUserDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ShopUserDto value)  $default,){
final _that = this;
switch (_that) {
case _ShopUserDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ShopUserDto value)?  $default,){
final _that = this;
switch (_that) {
case _ShopUserDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'userId')  String userId, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'email')  String? email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isLoginEnabled')  bool isLoginEnabled, @JsonKey(name: 'shopIds')  List<String> shopIds)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ShopUserDto() when $default != null:
return $default(_that.userId,_that.firstName,_that.lastName,_that.email,_that.phoneNumber,_that.role,_that.isLoginEnabled,_that.shopIds);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'userId')  String userId, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'email')  String? email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isLoginEnabled')  bool isLoginEnabled, @JsonKey(name: 'shopIds')  List<String> shopIds)  $default,) {final _that = this;
switch (_that) {
case _ShopUserDto():
return $default(_that.userId,_that.firstName,_that.lastName,_that.email,_that.phoneNumber,_that.role,_that.isLoginEnabled,_that.shopIds);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'userId')  String userId, @JsonKey(name: 'firstName')  String firstName, @JsonKey(name: 'lastName')  String lastName, @JsonKey(name: 'email')  String? email, @JsonKey(name: 'phoneNumber')  String? phoneNumber, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isLoginEnabled')  bool isLoginEnabled, @JsonKey(name: 'shopIds')  List<String> shopIds)?  $default,) {final _that = this;
switch (_that) {
case _ShopUserDto() when $default != null:
return $default(_that.userId,_that.firstName,_that.lastName,_that.email,_that.phoneNumber,_that.role,_that.isLoginEnabled,_that.shopIds);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ShopUserDto implements ShopUserDto {
  const _ShopUserDto({@JsonKey(name: 'userId') required this.userId, @JsonKey(name: 'firstName') required this.firstName, @JsonKey(name: 'lastName') required this.lastName, @JsonKey(name: 'email') this.email, @JsonKey(name: 'phoneNumber') this.phoneNumber, @JsonKey(name: 'role') required this.role, @JsonKey(name: 'isLoginEnabled') required this.isLoginEnabled, @JsonKey(name: 'shopIds') final  List<String> shopIds = const []}): _shopIds = shopIds;
  factory _ShopUserDto.fromJson(Map<String, dynamic> json) => _$ShopUserDtoFromJson(json);

@override@JsonKey(name: 'userId') final  String userId;
@override@JsonKey(name: 'firstName') final  String firstName;
@override@JsonKey(name: 'lastName') final  String lastName;
@override@JsonKey(name: 'email') final  String? email;
@override@JsonKey(name: 'phoneNumber') final  String? phoneNumber;
@override@JsonKey(name: 'role') final  String role;
@override@JsonKey(name: 'isLoginEnabled') final  bool isLoginEnabled;
 final  List<String> _shopIds;
@override@JsonKey(name: 'shopIds') List<String> get shopIds {
  if (_shopIds is EqualUnmodifiableListView) return _shopIds;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_shopIds);
}


/// Create a copy of ShopUserDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ShopUserDtoCopyWith<_ShopUserDto> get copyWith => __$ShopUserDtoCopyWithImpl<_ShopUserDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ShopUserDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ShopUserDto&&(identical(other.userId, userId) || other.userId == userId)&&(identical(other.firstName, firstName) || other.firstName == firstName)&&(identical(other.lastName, lastName) || other.lastName == lastName)&&(identical(other.email, email) || other.email == email)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.role, role) || other.role == role)&&(identical(other.isLoginEnabled, isLoginEnabled) || other.isLoginEnabled == isLoginEnabled)&&const DeepCollectionEquality().equals(other._shopIds, _shopIds));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,userId,firstName,lastName,email,phoneNumber,role,isLoginEnabled,const DeepCollectionEquality().hash(_shopIds));

@override
String toString() {
  return 'ShopUserDto(userId: $userId, firstName: $firstName, lastName: $lastName, email: $email, phoneNumber: $phoneNumber, role: $role, isLoginEnabled: $isLoginEnabled, shopIds: $shopIds)';
}


}

/// @nodoc
abstract mixin class _$ShopUserDtoCopyWith<$Res> implements $ShopUserDtoCopyWith<$Res> {
  factory _$ShopUserDtoCopyWith(_ShopUserDto value, $Res Function(_ShopUserDto) _then) = __$ShopUserDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'userId') String userId,@JsonKey(name: 'firstName') String firstName,@JsonKey(name: 'lastName') String lastName,@JsonKey(name: 'email') String? email,@JsonKey(name: 'phoneNumber') String? phoneNumber,@JsonKey(name: 'role') String role,@JsonKey(name: 'isLoginEnabled') bool isLoginEnabled,@JsonKey(name: 'shopIds') List<String> shopIds
});




}
/// @nodoc
class __$ShopUserDtoCopyWithImpl<$Res>
    implements _$ShopUserDtoCopyWith<$Res> {
  __$ShopUserDtoCopyWithImpl(this._self, this._then);

  final _ShopUserDto _self;
  final $Res Function(_ShopUserDto) _then;

/// Create a copy of ShopUserDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? userId = null,Object? firstName = null,Object? lastName = null,Object? email = freezed,Object? phoneNumber = freezed,Object? role = null,Object? isLoginEnabled = null,Object? shopIds = null,}) {
  return _then(_ShopUserDto(
userId: null == userId ? _self.userId : userId // ignore: cast_nullable_to_non_nullable
as String,firstName: null == firstName ? _self.firstName : firstName // ignore: cast_nullable_to_non_nullable
as String,lastName: null == lastName ? _self.lastName : lastName // ignore: cast_nullable_to_non_nullable
as String,email: freezed == email ? _self.email : email // ignore: cast_nullable_to_non_nullable
as String?,phoneNumber: freezed == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String?,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,isLoginEnabled: null == isLoginEnabled ? _self.isLoginEnabled : isLoginEnabled // ignore: cast_nullable_to_non_nullable
as bool,shopIds: null == shopIds ? _self._shopIds : shopIds // ignore: cast_nullable_to_non_nullable
as List<String>,
  ));
}


}

// dart format on
