// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'void_sale_return_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$VoidSaleReturnRequestDto {

@JsonKey(name: 'reason') String get reason;
/// Create a copy of VoidSaleReturnRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$VoidSaleReturnRequestDtoCopyWith<VoidSaleReturnRequestDto> get copyWith => _$VoidSaleReturnRequestDtoCopyWithImpl<VoidSaleReturnRequestDto>(this as VoidSaleReturnRequestDto, _$identity);

  /// Serializes this VoidSaleReturnRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is VoidSaleReturnRequestDto&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,reason);

@override
String toString() {
  return 'VoidSaleReturnRequestDto(reason: $reason)';
}


}

/// @nodoc
abstract mixin class $VoidSaleReturnRequestDtoCopyWith<$Res>  {
  factory $VoidSaleReturnRequestDtoCopyWith(VoidSaleReturnRequestDto value, $Res Function(VoidSaleReturnRequestDto) _then) = _$VoidSaleReturnRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'reason') String reason
});




}
/// @nodoc
class _$VoidSaleReturnRequestDtoCopyWithImpl<$Res>
    implements $VoidSaleReturnRequestDtoCopyWith<$Res> {
  _$VoidSaleReturnRequestDtoCopyWithImpl(this._self, this._then);

  final VoidSaleReturnRequestDto _self;
  final $Res Function(VoidSaleReturnRequestDto) _then;

/// Create a copy of VoidSaleReturnRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? reason = null,}) {
  return _then(_self.copyWith(
reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [VoidSaleReturnRequestDto].
extension VoidSaleReturnRequestDtoPatterns on VoidSaleReturnRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _VoidSaleReturnRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _VoidSaleReturnRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _VoidSaleReturnRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _VoidSaleReturnRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _VoidSaleReturnRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _VoidSaleReturnRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'reason')  String reason)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _VoidSaleReturnRequestDto() when $default != null:
return $default(_that.reason);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'reason')  String reason)  $default,) {final _that = this;
switch (_that) {
case _VoidSaleReturnRequestDto():
return $default(_that.reason);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'reason')  String reason)?  $default,) {final _that = this;
switch (_that) {
case _VoidSaleReturnRequestDto() when $default != null:
return $default(_that.reason);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _VoidSaleReturnRequestDto implements VoidSaleReturnRequestDto {
  const _VoidSaleReturnRequestDto({@JsonKey(name: 'reason') required this.reason});
  factory _VoidSaleReturnRequestDto.fromJson(Map<String, dynamic> json) => _$VoidSaleReturnRequestDtoFromJson(json);

@override@JsonKey(name: 'reason') final  String reason;

/// Create a copy of VoidSaleReturnRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$VoidSaleReturnRequestDtoCopyWith<_VoidSaleReturnRequestDto> get copyWith => __$VoidSaleReturnRequestDtoCopyWithImpl<_VoidSaleReturnRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$VoidSaleReturnRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _VoidSaleReturnRequestDto&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,reason);

@override
String toString() {
  return 'VoidSaleReturnRequestDto(reason: $reason)';
}


}

/// @nodoc
abstract mixin class _$VoidSaleReturnRequestDtoCopyWith<$Res> implements $VoidSaleReturnRequestDtoCopyWith<$Res> {
  factory _$VoidSaleReturnRequestDtoCopyWith(_VoidSaleReturnRequestDto value, $Res Function(_VoidSaleReturnRequestDto) _then) = __$VoidSaleReturnRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'reason') String reason
});




}
/// @nodoc
class __$VoidSaleReturnRequestDtoCopyWithImpl<$Res>
    implements _$VoidSaleReturnRequestDtoCopyWith<$Res> {
  __$VoidSaleReturnRequestDtoCopyWithImpl(this._self, this._then);

  final _VoidSaleReturnRequestDto _self;
  final $Res Function(_VoidSaleReturnRequestDto) _then;

/// Create a copy of VoidSaleReturnRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? reason = null,}) {
  return _then(_VoidSaleReturnRequestDto(
reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
