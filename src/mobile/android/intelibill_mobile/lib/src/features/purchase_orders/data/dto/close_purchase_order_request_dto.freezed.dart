// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'close_purchase_order_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ClosePurchaseOrderRequestDto {

 String get reason;
/// Create a copy of ClosePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ClosePurchaseOrderRequestDtoCopyWith<ClosePurchaseOrderRequestDto> get copyWith => _$ClosePurchaseOrderRequestDtoCopyWithImpl<ClosePurchaseOrderRequestDto>(this as ClosePurchaseOrderRequestDto, _$identity);

  /// Serializes this ClosePurchaseOrderRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ClosePurchaseOrderRequestDto&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,reason);

@override
String toString() {
  return 'ClosePurchaseOrderRequestDto(reason: $reason)';
}


}

/// @nodoc
abstract mixin class $ClosePurchaseOrderRequestDtoCopyWith<$Res>  {
  factory $ClosePurchaseOrderRequestDtoCopyWith(ClosePurchaseOrderRequestDto value, $Res Function(ClosePurchaseOrderRequestDto) _then) = _$ClosePurchaseOrderRequestDtoCopyWithImpl;
@useResult
$Res call({
 String reason
});




}
/// @nodoc
class _$ClosePurchaseOrderRequestDtoCopyWithImpl<$Res>
    implements $ClosePurchaseOrderRequestDtoCopyWith<$Res> {
  _$ClosePurchaseOrderRequestDtoCopyWithImpl(this._self, this._then);

  final ClosePurchaseOrderRequestDto _self;
  final $Res Function(ClosePurchaseOrderRequestDto) _then;

/// Create a copy of ClosePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? reason = null,}) {
  return _then(_self.copyWith(
reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [ClosePurchaseOrderRequestDto].
extension ClosePurchaseOrderRequestDtoPatterns on ClosePurchaseOrderRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ClosePurchaseOrderRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ClosePurchaseOrderRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ClosePurchaseOrderRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _ClosePurchaseOrderRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ClosePurchaseOrderRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _ClosePurchaseOrderRequestDto() when $default != null:
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
case _ClosePurchaseOrderRequestDto() when $default != null:
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
case _ClosePurchaseOrderRequestDto():
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
case _ClosePurchaseOrderRequestDto() when $default != null:
return $default(_that.reason);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ClosePurchaseOrderRequestDto implements ClosePurchaseOrderRequestDto {
  const _ClosePurchaseOrderRequestDto({required this.reason});
  factory _ClosePurchaseOrderRequestDto.fromJson(Map<String, dynamic> json) => _$ClosePurchaseOrderRequestDtoFromJson(json);

@override final  String reason;

/// Create a copy of ClosePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ClosePurchaseOrderRequestDtoCopyWith<_ClosePurchaseOrderRequestDto> get copyWith => __$ClosePurchaseOrderRequestDtoCopyWithImpl<_ClosePurchaseOrderRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ClosePurchaseOrderRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ClosePurchaseOrderRequestDto&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,reason);

@override
String toString() {
  return 'ClosePurchaseOrderRequestDto(reason: $reason)';
}


}

/// @nodoc
abstract mixin class _$ClosePurchaseOrderRequestDtoCopyWith<$Res> implements $ClosePurchaseOrderRequestDtoCopyWith<$Res> {
  factory _$ClosePurchaseOrderRequestDtoCopyWith(_ClosePurchaseOrderRequestDto value, $Res Function(_ClosePurchaseOrderRequestDto) _then) = __$ClosePurchaseOrderRequestDtoCopyWithImpl;
@override @useResult
$Res call({
 String reason
});




}
/// @nodoc
class __$ClosePurchaseOrderRequestDtoCopyWithImpl<$Res>
    implements _$ClosePurchaseOrderRequestDtoCopyWith<$Res> {
  __$ClosePurchaseOrderRequestDtoCopyWithImpl(this._self, this._then);

  final _ClosePurchaseOrderRequestDto _self;
  final $Res Function(_ClosePurchaseOrderRequestDto) _then;

/// Create a copy of ClosePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? reason = null,}) {
  return _then(_ClosePurchaseOrderRequestDto(
reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
