// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'user_shop_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$UserShopDto {

@JsonKey(name: 'shopId') String get shopId;@JsonKey(name: 'shopName') String get shopName;@JsonKey(name: 'role') String get role;@JsonKey(name: 'isDefault') bool get isDefault;@JsonKey(name: 'lastUsedAt') DateTime? get lastUsedAt;
/// Create a copy of UserShopDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$UserShopDtoCopyWith<UserShopDto> get copyWith => _$UserShopDtoCopyWithImpl<UserShopDto>(this as UserShopDto, _$identity);

  /// Serializes this UserShopDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is UserShopDto&&(identical(other.shopId, shopId) || other.shopId == shopId)&&(identical(other.shopName, shopName) || other.shopName == shopName)&&(identical(other.role, role) || other.role == role)&&(identical(other.isDefault, isDefault) || other.isDefault == isDefault)&&(identical(other.lastUsedAt, lastUsedAt) || other.lastUsedAt == lastUsedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,shopId,shopName,role,isDefault,lastUsedAt);

@override
String toString() {
  return 'UserShopDto(shopId: $shopId, shopName: $shopName, role: $role, isDefault: $isDefault, lastUsedAt: $lastUsedAt)';
}


}

/// @nodoc
abstract mixin class $UserShopDtoCopyWith<$Res>  {
  factory $UserShopDtoCopyWith(UserShopDto value, $Res Function(UserShopDto) _then) = _$UserShopDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'shopId') String shopId,@JsonKey(name: 'shopName') String shopName,@JsonKey(name: 'role') String role,@JsonKey(name: 'isDefault') bool isDefault,@JsonKey(name: 'lastUsedAt') DateTime? lastUsedAt
});




}
/// @nodoc
class _$UserShopDtoCopyWithImpl<$Res>
    implements $UserShopDtoCopyWith<$Res> {
  _$UserShopDtoCopyWithImpl(this._self, this._then);

  final UserShopDto _self;
  final $Res Function(UserShopDto) _then;

/// Create a copy of UserShopDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? shopId = null,Object? shopName = null,Object? role = null,Object? isDefault = null,Object? lastUsedAt = freezed,}) {
  return _then(_self.copyWith(
shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,shopName: null == shopName ? _self.shopName : shopName // ignore: cast_nullable_to_non_nullable
as String,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,isDefault: null == isDefault ? _self.isDefault : isDefault // ignore: cast_nullable_to_non_nullable
as bool,lastUsedAt: freezed == lastUsedAt ? _self.lastUsedAt : lastUsedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,
  ));
}

}


/// Adds pattern-matching-related methods to [UserShopDto].
extension UserShopDtoPatterns on UserShopDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _UserShopDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _UserShopDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _UserShopDto value)  $default,){
final _that = this;
switch (_that) {
case _UserShopDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _UserShopDto value)?  $default,){
final _that = this;
switch (_that) {
case _UserShopDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopId')  String shopId, @JsonKey(name: 'shopName')  String shopName, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isDefault')  bool isDefault, @JsonKey(name: 'lastUsedAt')  DateTime? lastUsedAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _UserShopDto() when $default != null:
return $default(_that.shopId,_that.shopName,_that.role,_that.isDefault,_that.lastUsedAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopId')  String shopId, @JsonKey(name: 'shopName')  String shopName, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isDefault')  bool isDefault, @JsonKey(name: 'lastUsedAt')  DateTime? lastUsedAt)  $default,) {final _that = this;
switch (_that) {
case _UserShopDto():
return $default(_that.shopId,_that.shopName,_that.role,_that.isDefault,_that.lastUsedAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'shopId')  String shopId, @JsonKey(name: 'shopName')  String shopName, @JsonKey(name: 'role')  String role, @JsonKey(name: 'isDefault')  bool isDefault, @JsonKey(name: 'lastUsedAt')  DateTime? lastUsedAt)?  $default,) {final _that = this;
switch (_that) {
case _UserShopDto() when $default != null:
return $default(_that.shopId,_that.shopName,_that.role,_that.isDefault,_that.lastUsedAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _UserShopDto implements UserShopDto {
  const _UserShopDto({@JsonKey(name: 'shopId') required this.shopId, @JsonKey(name: 'shopName') required this.shopName, @JsonKey(name: 'role') required this.role, @JsonKey(name: 'isDefault') required this.isDefault, @JsonKey(name: 'lastUsedAt') this.lastUsedAt});
  factory _UserShopDto.fromJson(Map<String, dynamic> json) => _$UserShopDtoFromJson(json);

@override@JsonKey(name: 'shopId') final  String shopId;
@override@JsonKey(name: 'shopName') final  String shopName;
@override@JsonKey(name: 'role') final  String role;
@override@JsonKey(name: 'isDefault') final  bool isDefault;
@override@JsonKey(name: 'lastUsedAt') final  DateTime? lastUsedAt;

/// Create a copy of UserShopDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$UserShopDtoCopyWith<_UserShopDto> get copyWith => __$UserShopDtoCopyWithImpl<_UserShopDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$UserShopDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _UserShopDto&&(identical(other.shopId, shopId) || other.shopId == shopId)&&(identical(other.shopName, shopName) || other.shopName == shopName)&&(identical(other.role, role) || other.role == role)&&(identical(other.isDefault, isDefault) || other.isDefault == isDefault)&&(identical(other.lastUsedAt, lastUsedAt) || other.lastUsedAt == lastUsedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,shopId,shopName,role,isDefault,lastUsedAt);

@override
String toString() {
  return 'UserShopDto(shopId: $shopId, shopName: $shopName, role: $role, isDefault: $isDefault, lastUsedAt: $lastUsedAt)';
}


}

/// @nodoc
abstract mixin class _$UserShopDtoCopyWith<$Res> implements $UserShopDtoCopyWith<$Res> {
  factory _$UserShopDtoCopyWith(_UserShopDto value, $Res Function(_UserShopDto) _then) = __$UserShopDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'shopId') String shopId,@JsonKey(name: 'shopName') String shopName,@JsonKey(name: 'role') String role,@JsonKey(name: 'isDefault') bool isDefault,@JsonKey(name: 'lastUsedAt') DateTime? lastUsedAt
});




}
/// @nodoc
class __$UserShopDtoCopyWithImpl<$Res>
    implements _$UserShopDtoCopyWith<$Res> {
  __$UserShopDtoCopyWithImpl(this._self, this._then);

  final _UserShopDto _self;
  final $Res Function(_UserShopDto) _then;

/// Create a copy of UserShopDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? shopId = null,Object? shopName = null,Object? role = null,Object? isDefault = null,Object? lastUsedAt = freezed,}) {
  return _then(_UserShopDto(
shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,shopName: null == shopName ? _self.shopName : shopName // ignore: cast_nullable_to_non_nullable
as String,role: null == role ? _self.role : role // ignore: cast_nullable_to_non_nullable
as String,isDefault: null == isDefault ? _self.isDefault : isDefault // ignore: cast_nullable_to_non_nullable
as bool,lastUsedAt: freezed == lastUsedAt ? _self.lastUsedAt : lastUsedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,
  ));
}


}

// dart format on
