// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'set_default_shop_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SetDefaultShopRequestDto {

@JsonKey(name: 'shopId') String get shopId;
/// Create a copy of SetDefaultShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SetDefaultShopRequestDtoCopyWith<SetDefaultShopRequestDto> get copyWith => _$SetDefaultShopRequestDtoCopyWithImpl<SetDefaultShopRequestDto>(this as SetDefaultShopRequestDto, _$identity);

  /// Serializes this SetDefaultShopRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SetDefaultShopRequestDto&&(identical(other.shopId, shopId) || other.shopId == shopId));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,shopId);

@override
String toString() {
  return 'SetDefaultShopRequestDto(shopId: $shopId)';
}


}

/// @nodoc
abstract mixin class $SetDefaultShopRequestDtoCopyWith<$Res>  {
  factory $SetDefaultShopRequestDtoCopyWith(SetDefaultShopRequestDto value, $Res Function(SetDefaultShopRequestDto) _then) = _$SetDefaultShopRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'shopId') String shopId
});




}
/// @nodoc
class _$SetDefaultShopRequestDtoCopyWithImpl<$Res>
    implements $SetDefaultShopRequestDtoCopyWith<$Res> {
  _$SetDefaultShopRequestDtoCopyWithImpl(this._self, this._then);

  final SetDefaultShopRequestDto _self;
  final $Res Function(SetDefaultShopRequestDto) _then;

/// Create a copy of SetDefaultShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? shopId = null,}) {
  return _then(_self.copyWith(
shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [SetDefaultShopRequestDto].
extension SetDefaultShopRequestDtoPatterns on SetDefaultShopRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SetDefaultShopRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SetDefaultShopRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SetDefaultShopRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _SetDefaultShopRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SetDefaultShopRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _SetDefaultShopRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopId')  String shopId)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SetDefaultShopRequestDto() when $default != null:
return $default(_that.shopId);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopId')  String shopId)  $default,) {final _that = this;
switch (_that) {
case _SetDefaultShopRequestDto():
return $default(_that.shopId);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'shopId')  String shopId)?  $default,) {final _that = this;
switch (_that) {
case _SetDefaultShopRequestDto() when $default != null:
return $default(_that.shopId);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SetDefaultShopRequestDto implements SetDefaultShopRequestDto {
  const _SetDefaultShopRequestDto({@JsonKey(name: 'shopId') required this.shopId});
  factory _SetDefaultShopRequestDto.fromJson(Map<String, dynamic> json) => _$SetDefaultShopRequestDtoFromJson(json);

@override@JsonKey(name: 'shopId') final  String shopId;

/// Create a copy of SetDefaultShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SetDefaultShopRequestDtoCopyWith<_SetDefaultShopRequestDto> get copyWith => __$SetDefaultShopRequestDtoCopyWithImpl<_SetDefaultShopRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SetDefaultShopRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SetDefaultShopRequestDto&&(identical(other.shopId, shopId) || other.shopId == shopId));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,shopId);

@override
String toString() {
  return 'SetDefaultShopRequestDto(shopId: $shopId)';
}


}

/// @nodoc
abstract mixin class _$SetDefaultShopRequestDtoCopyWith<$Res> implements $SetDefaultShopRequestDtoCopyWith<$Res> {
  factory _$SetDefaultShopRequestDtoCopyWith(_SetDefaultShopRequestDto value, $Res Function(_SetDefaultShopRequestDto) _then) = __$SetDefaultShopRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'shopId') String shopId
});




}
/// @nodoc
class __$SetDefaultShopRequestDtoCopyWithImpl<$Res>
    implements _$SetDefaultShopRequestDtoCopyWith<$Res> {
  __$SetDefaultShopRequestDtoCopyWithImpl(this._self, this._then);

  final _SetDefaultShopRequestDto _self;
  final $Res Function(_SetDefaultShopRequestDto) _then;

/// Create a copy of SetDefaultShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? shopId = null,}) {
  return _then(_SetDefaultShopRequestDto(
shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
