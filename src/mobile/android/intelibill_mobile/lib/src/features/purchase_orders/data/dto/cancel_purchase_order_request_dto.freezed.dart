// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'cancel_purchase_order_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CancelPurchaseOrderRequestDto {

 String get reason;
/// Create a copy of CancelPurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CancelPurchaseOrderRequestDtoCopyWith<CancelPurchaseOrderRequestDto> get copyWith => _$CancelPurchaseOrderRequestDtoCopyWithImpl<CancelPurchaseOrderRequestDto>(this as CancelPurchaseOrderRequestDto, _$identity);

  /// Serializes this CancelPurchaseOrderRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CancelPurchaseOrderRequestDto&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,reason);

@override
String toString() {
  return 'CancelPurchaseOrderRequestDto(reason: $reason)';
}


}

/// @nodoc
abstract mixin class $CancelPurchaseOrderRequestDtoCopyWith<$Res>  {
  factory $CancelPurchaseOrderRequestDtoCopyWith(CancelPurchaseOrderRequestDto value, $Res Function(CancelPurchaseOrderRequestDto) _then) = _$CancelPurchaseOrderRequestDtoCopyWithImpl;
@useResult
$Res call({
 String reason
});




}
/// @nodoc
class _$CancelPurchaseOrderRequestDtoCopyWithImpl<$Res>
    implements $CancelPurchaseOrderRequestDtoCopyWith<$Res> {
  _$CancelPurchaseOrderRequestDtoCopyWithImpl(this._self, this._then);

  final CancelPurchaseOrderRequestDto _self;
  final $Res Function(CancelPurchaseOrderRequestDto) _then;

/// Create a copy of CancelPurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? reason = null,}) {
  return _then(_self.copyWith(
reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [CancelPurchaseOrderRequestDto].
extension CancelPurchaseOrderRequestDtoPatterns on CancelPurchaseOrderRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CancelPurchaseOrderRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CancelPurchaseOrderRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CancelPurchaseOrderRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _CancelPurchaseOrderRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CancelPurchaseOrderRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _CancelPurchaseOrderRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String reason)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CancelPurchaseOrderRequestDto() when $default != null:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String reason)  $default,) {final _that = this;
switch (_that) {
case _CancelPurchaseOrderRequestDto():
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String reason)?  $default,) {final _that = this;
switch (_that) {
case _CancelPurchaseOrderRequestDto() when $default != null:
return $default(_that.reason);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CancelPurchaseOrderRequestDto implements CancelPurchaseOrderRequestDto {
  const _CancelPurchaseOrderRequestDto({required this.reason});
  factory _CancelPurchaseOrderRequestDto.fromJson(Map<String, dynamic> json) => _$CancelPurchaseOrderRequestDtoFromJson(json);

@override final  String reason;

/// Create a copy of CancelPurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CancelPurchaseOrderRequestDtoCopyWith<_CancelPurchaseOrderRequestDto> get copyWith => __$CancelPurchaseOrderRequestDtoCopyWithImpl<_CancelPurchaseOrderRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CancelPurchaseOrderRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CancelPurchaseOrderRequestDto&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,reason);

@override
String toString() {
  return 'CancelPurchaseOrderRequestDto(reason: $reason)';
}


}

/// @nodoc
abstract mixin class _$CancelPurchaseOrderRequestDtoCopyWith<$Res> implements $CancelPurchaseOrderRequestDtoCopyWith<$Res> {
  factory _$CancelPurchaseOrderRequestDtoCopyWith(_CancelPurchaseOrderRequestDto value, $Res Function(_CancelPurchaseOrderRequestDto) _then) = __$CancelPurchaseOrderRequestDtoCopyWithImpl;
@override @useResult
$Res call({
 String reason
});




}
/// @nodoc
class __$CancelPurchaseOrderRequestDtoCopyWithImpl<$Res>
    implements _$CancelPurchaseOrderRequestDtoCopyWith<$Res> {
  __$CancelPurchaseOrderRequestDtoCopyWithImpl(this._self, this._then);

  final _CancelPurchaseOrderRequestDto _self;
  final $Res Function(_CancelPurchaseOrderRequestDto) _then;

/// Create a copy of CancelPurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? reason = null,}) {
  return _then(_CancelPurchaseOrderRequestDto(
reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
