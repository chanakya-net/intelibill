// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'auth_result_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AuthResultDto {

@JsonKey(name: 'accessToken') String get accessToken;@JsonKey(name: 'refreshToken') String get refreshToken;@JsonKey(name: 'accessTokenExpiresAt') DateTime get accessTokenExpiresAt;@JsonKey(name: 'refreshTokenExpiresAt') DateTime get refreshTokenExpiresAt;@JsonKey(name: 'user') AuthUserDto get user;@JsonKey(name: 'activeShopId') String? get activeShopId;@JsonKey(name: 'shops') List<UserShopDto>? get shops;
/// Create a copy of AuthResultDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AuthResultDtoCopyWith<AuthResultDto> get copyWith => _$AuthResultDtoCopyWithImpl<AuthResultDto>(this as AuthResultDto, _$identity);

  /// Serializes this AuthResultDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AuthResultDto&&(identical(other.accessToken, accessToken) || other.accessToken == accessToken)&&(identical(other.refreshToken, refreshToken) || other.refreshToken == refreshToken)&&(identical(other.accessTokenExpiresAt, accessTokenExpiresAt) || other.accessTokenExpiresAt == accessTokenExpiresAt)&&(identical(other.refreshTokenExpiresAt, refreshTokenExpiresAt) || other.refreshTokenExpiresAt == refreshTokenExpiresAt)&&(identical(other.user, user) || other.user == user)&&(identical(other.activeShopId, activeShopId) || other.activeShopId == activeShopId)&&const DeepCollectionEquality().equals(other.shops, shops));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,accessToken,refreshToken,accessTokenExpiresAt,refreshTokenExpiresAt,user,activeShopId,const DeepCollectionEquality().hash(shops));

@override
String toString() {
  return 'AuthResultDto(accessToken: $accessToken, refreshToken: $refreshToken, accessTokenExpiresAt: $accessTokenExpiresAt, refreshTokenExpiresAt: $refreshTokenExpiresAt, user: $user, activeShopId: $activeShopId, shops: $shops)';
}


}

/// @nodoc
abstract mixin class $AuthResultDtoCopyWith<$Res>  {
  factory $AuthResultDtoCopyWith(AuthResultDto value, $Res Function(AuthResultDto) _then) = _$AuthResultDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'accessToken') String accessToken,@JsonKey(name: 'refreshToken') String refreshToken,@JsonKey(name: 'accessTokenExpiresAt') DateTime accessTokenExpiresAt,@JsonKey(name: 'refreshTokenExpiresAt') DateTime refreshTokenExpiresAt,@JsonKey(name: 'user') AuthUserDto user,@JsonKey(name: 'activeShopId') String? activeShopId,@JsonKey(name: 'shops') List<UserShopDto>? shops
});


$AuthUserDtoCopyWith<$Res> get user;

}
/// @nodoc
class _$AuthResultDtoCopyWithImpl<$Res>
    implements $AuthResultDtoCopyWith<$Res> {
  _$AuthResultDtoCopyWithImpl(this._self, this._then);

  final AuthResultDto _self;
  final $Res Function(AuthResultDto) _then;

/// Create a copy of AuthResultDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? accessToken = null,Object? refreshToken = null,Object? accessTokenExpiresAt = null,Object? refreshTokenExpiresAt = null,Object? user = null,Object? activeShopId = freezed,Object? shops = freezed,}) {
  return _then(_self.copyWith(
accessToken: null == accessToken ? _self.accessToken : accessToken // ignore: cast_nullable_to_non_nullable
as String,refreshToken: null == refreshToken ? _self.refreshToken : refreshToken // ignore: cast_nullable_to_non_nullable
as String,accessTokenExpiresAt: null == accessTokenExpiresAt ? _self.accessTokenExpiresAt : accessTokenExpiresAt // ignore: cast_nullable_to_non_nullable
as DateTime,refreshTokenExpiresAt: null == refreshTokenExpiresAt ? _self.refreshTokenExpiresAt : refreshTokenExpiresAt // ignore: cast_nullable_to_non_nullable
as DateTime,user: null == user ? _self.user : user // ignore: cast_nullable_to_non_nullable
as AuthUserDto,activeShopId: freezed == activeShopId ? _self.activeShopId : activeShopId // ignore: cast_nullable_to_non_nullable
as String?,shops: freezed == shops ? _self.shops : shops // ignore: cast_nullable_to_non_nullable
as List<UserShopDto>?,
  ));
}
/// Create a copy of AuthResultDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$AuthUserDtoCopyWith<$Res> get user {
  
  return $AuthUserDtoCopyWith<$Res>(_self.user, (value) {
    return _then(_self.copyWith(user: value));
  });
}
}


/// Adds pattern-matching-related methods to [AuthResultDto].
extension AuthResultDtoPatterns on AuthResultDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AuthResultDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AuthResultDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AuthResultDto value)  $default,){
final _that = this;
switch (_that) {
case _AuthResultDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AuthResultDto value)?  $default,){
final _that = this;
switch (_that) {
case _AuthResultDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'accessToken')  String accessToken, @JsonKey(name: 'refreshToken')  String refreshToken, @JsonKey(name: 'accessTokenExpiresAt')  DateTime accessTokenExpiresAt, @JsonKey(name: 'refreshTokenExpiresAt')  DateTime refreshTokenExpiresAt, @JsonKey(name: 'user')  AuthUserDto user, @JsonKey(name: 'activeShopId')  String? activeShopId, @JsonKey(name: 'shops')  List<UserShopDto>? shops)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AuthResultDto() when $default != null:
return $default(_that.accessToken,_that.refreshToken,_that.accessTokenExpiresAt,_that.refreshTokenExpiresAt,_that.user,_that.activeShopId,_that.shops);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'accessToken')  String accessToken, @JsonKey(name: 'refreshToken')  String refreshToken, @JsonKey(name: 'accessTokenExpiresAt')  DateTime accessTokenExpiresAt, @JsonKey(name: 'refreshTokenExpiresAt')  DateTime refreshTokenExpiresAt, @JsonKey(name: 'user')  AuthUserDto user, @JsonKey(name: 'activeShopId')  String? activeShopId, @JsonKey(name: 'shops')  List<UserShopDto>? shops)  $default,) {final _that = this;
switch (_that) {
case _AuthResultDto():
return $default(_that.accessToken,_that.refreshToken,_that.accessTokenExpiresAt,_that.refreshTokenExpiresAt,_that.user,_that.activeShopId,_that.shops);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'accessToken')  String accessToken, @JsonKey(name: 'refreshToken')  String refreshToken, @JsonKey(name: 'accessTokenExpiresAt')  DateTime accessTokenExpiresAt, @JsonKey(name: 'refreshTokenExpiresAt')  DateTime refreshTokenExpiresAt, @JsonKey(name: 'user')  AuthUserDto user, @JsonKey(name: 'activeShopId')  String? activeShopId, @JsonKey(name: 'shops')  List<UserShopDto>? shops)?  $default,) {final _that = this;
switch (_that) {
case _AuthResultDto() when $default != null:
return $default(_that.accessToken,_that.refreshToken,_that.accessTokenExpiresAt,_that.refreshTokenExpiresAt,_that.user,_that.activeShopId,_that.shops);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AuthResultDto implements AuthResultDto {
  const _AuthResultDto({@JsonKey(name: 'accessToken') required this.accessToken, @JsonKey(name: 'refreshToken') required this.refreshToken, @JsonKey(name: 'accessTokenExpiresAt') required this.accessTokenExpiresAt, @JsonKey(name: 'refreshTokenExpiresAt') required this.refreshTokenExpiresAt, @JsonKey(name: 'user') required this.user, @JsonKey(name: 'activeShopId') this.activeShopId, @JsonKey(name: 'shops') final  List<UserShopDto>? shops}): _shops = shops;
  factory _AuthResultDto.fromJson(Map<String, dynamic> json) => _$AuthResultDtoFromJson(json);

@override@JsonKey(name: 'accessToken') final  String accessToken;
@override@JsonKey(name: 'refreshToken') final  String refreshToken;
@override@JsonKey(name: 'accessTokenExpiresAt') final  DateTime accessTokenExpiresAt;
@override@JsonKey(name: 'refreshTokenExpiresAt') final  DateTime refreshTokenExpiresAt;
@override@JsonKey(name: 'user') final  AuthUserDto user;
@override@JsonKey(name: 'activeShopId') final  String? activeShopId;
 final  List<UserShopDto>? _shops;
@override@JsonKey(name: 'shops') List<UserShopDto>? get shops {
  final value = _shops;
  if (value == null) return null;
  if (_shops is EqualUnmodifiableListView) return _shops;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}


/// Create a copy of AuthResultDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AuthResultDtoCopyWith<_AuthResultDto> get copyWith => __$AuthResultDtoCopyWithImpl<_AuthResultDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AuthResultDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AuthResultDto&&(identical(other.accessToken, accessToken) || other.accessToken == accessToken)&&(identical(other.refreshToken, refreshToken) || other.refreshToken == refreshToken)&&(identical(other.accessTokenExpiresAt, accessTokenExpiresAt) || other.accessTokenExpiresAt == accessTokenExpiresAt)&&(identical(other.refreshTokenExpiresAt, refreshTokenExpiresAt) || other.refreshTokenExpiresAt == refreshTokenExpiresAt)&&(identical(other.user, user) || other.user == user)&&(identical(other.activeShopId, activeShopId) || other.activeShopId == activeShopId)&&const DeepCollectionEquality().equals(other._shops, _shops));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,accessToken,refreshToken,accessTokenExpiresAt,refreshTokenExpiresAt,user,activeShopId,const DeepCollectionEquality().hash(_shops));

@override
String toString() {
  return 'AuthResultDto(accessToken: $accessToken, refreshToken: $refreshToken, accessTokenExpiresAt: $accessTokenExpiresAt, refreshTokenExpiresAt: $refreshTokenExpiresAt, user: $user, activeShopId: $activeShopId, shops: $shops)';
}


}

/// @nodoc
abstract mixin class _$AuthResultDtoCopyWith<$Res> implements $AuthResultDtoCopyWith<$Res> {
  factory _$AuthResultDtoCopyWith(_AuthResultDto value, $Res Function(_AuthResultDto) _then) = __$AuthResultDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'accessToken') String accessToken,@JsonKey(name: 'refreshToken') String refreshToken,@JsonKey(name: 'accessTokenExpiresAt') DateTime accessTokenExpiresAt,@JsonKey(name: 'refreshTokenExpiresAt') DateTime refreshTokenExpiresAt,@JsonKey(name: 'user') AuthUserDto user,@JsonKey(name: 'activeShopId') String? activeShopId,@JsonKey(name: 'shops') List<UserShopDto>? shops
});


@override $AuthUserDtoCopyWith<$Res> get user;

}
/// @nodoc
class __$AuthResultDtoCopyWithImpl<$Res>
    implements _$AuthResultDtoCopyWith<$Res> {
  __$AuthResultDtoCopyWithImpl(this._self, this._then);

  final _AuthResultDto _self;
  final $Res Function(_AuthResultDto) _then;

/// Create a copy of AuthResultDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? accessToken = null,Object? refreshToken = null,Object? accessTokenExpiresAt = null,Object? refreshTokenExpiresAt = null,Object? user = null,Object? activeShopId = freezed,Object? shops = freezed,}) {
  return _then(_AuthResultDto(
accessToken: null == accessToken ? _self.accessToken : accessToken // ignore: cast_nullable_to_non_nullable
as String,refreshToken: null == refreshToken ? _self.refreshToken : refreshToken // ignore: cast_nullable_to_non_nullable
as String,accessTokenExpiresAt: null == accessTokenExpiresAt ? _self.accessTokenExpiresAt : accessTokenExpiresAt // ignore: cast_nullable_to_non_nullable
as DateTime,refreshTokenExpiresAt: null == refreshTokenExpiresAt ? _self.refreshTokenExpiresAt : refreshTokenExpiresAt // ignore: cast_nullable_to_non_nullable
as DateTime,user: null == user ? _self.user : user // ignore: cast_nullable_to_non_nullable
as AuthUserDto,activeShopId: freezed == activeShopId ? _self.activeShopId : activeShopId // ignore: cast_nullable_to_non_nullable
as String?,shops: freezed == shops ? _self._shops : shops // ignore: cast_nullable_to_non_nullable
as List<UserShopDto>?,
  ));
}

/// Create a copy of AuthResultDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$AuthUserDtoCopyWith<$Res> get user {
  
  return $AuthUserDtoCopyWith<$Res>(_self.user, (value) {
    return _then(_self.copyWith(user: value));
  });
}
}

// dart format on
