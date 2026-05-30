// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'adjust_inventory_batch_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AdjustInventoryBatchRequestDto {

@JsonKey(name: 'direction') String get direction;@JsonKey(name: 'reason') String get reason;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'performedAt') String? get performedAt;@JsonKey(name: 'notes') String? get notes;
/// Create a copy of AdjustInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AdjustInventoryBatchRequestDtoCopyWith<AdjustInventoryBatchRequestDto> get copyWith => _$AdjustInventoryBatchRequestDtoCopyWithImpl<AdjustInventoryBatchRequestDto>(this as AdjustInventoryBatchRequestDto, _$identity);

  /// Serializes this AdjustInventoryBatchRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AdjustInventoryBatchRequestDto&&(identical(other.direction, direction) || other.direction == direction)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt)&&(identical(other.notes, notes) || other.notes == notes));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,direction,reason,quantity,performedAt,notes);

@override
String toString() {
  return 'AdjustInventoryBatchRequestDto(direction: $direction, reason: $reason, quantity: $quantity, performedAt: $performedAt, notes: $notes)';
}


}

/// @nodoc
abstract mixin class $AdjustInventoryBatchRequestDtoCopyWith<$Res>  {
  factory $AdjustInventoryBatchRequestDtoCopyWith(AdjustInventoryBatchRequestDto value, $Res Function(AdjustInventoryBatchRequestDto) _then) = _$AdjustInventoryBatchRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'direction') String direction,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'performedAt') String? performedAt,@JsonKey(name: 'notes') String? notes
});




}
/// @nodoc
class _$AdjustInventoryBatchRequestDtoCopyWithImpl<$Res>
    implements $AdjustInventoryBatchRequestDtoCopyWith<$Res> {
  _$AdjustInventoryBatchRequestDtoCopyWithImpl(this._self, this._then);

  final AdjustInventoryBatchRequestDto _self;
  final $Res Function(AdjustInventoryBatchRequestDto) _then;

/// Create a copy of AdjustInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? direction = null,Object? reason = null,Object? quantity = null,Object? performedAt = freezed,Object? notes = freezed,}) {
  return _then(_self.copyWith(
direction: null == direction ? _self.direction : direction // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,performedAt: freezed == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [AdjustInventoryBatchRequestDto].
extension AdjustInventoryBatchRequestDtoPatterns on AdjustInventoryBatchRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AdjustInventoryBatchRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AdjustInventoryBatchRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AdjustInventoryBatchRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _AdjustInventoryBatchRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AdjustInventoryBatchRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _AdjustInventoryBatchRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'direction')  String direction, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'performedAt')  String? performedAt, @JsonKey(name: 'notes')  String? notes)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AdjustInventoryBatchRequestDto() when $default != null:
return $default(_that.direction,_that.reason,_that.quantity,_that.performedAt,_that.notes);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'direction')  String direction, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'performedAt')  String? performedAt, @JsonKey(name: 'notes')  String? notes)  $default,) {final _that = this;
switch (_that) {
case _AdjustInventoryBatchRequestDto():
return $default(_that.direction,_that.reason,_that.quantity,_that.performedAt,_that.notes);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'direction')  String direction, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'performedAt')  String? performedAt, @JsonKey(name: 'notes')  String? notes)?  $default,) {final _that = this;
switch (_that) {
case _AdjustInventoryBatchRequestDto() when $default != null:
return $default(_that.direction,_that.reason,_that.quantity,_that.performedAt,_that.notes);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AdjustInventoryBatchRequestDto implements AdjustInventoryBatchRequestDto {
  const _AdjustInventoryBatchRequestDto({@JsonKey(name: 'direction') required this.direction, @JsonKey(name: 'reason') required this.reason, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'performedAt') this.performedAt, @JsonKey(name: 'notes') this.notes});
  factory _AdjustInventoryBatchRequestDto.fromJson(Map<String, dynamic> json) => _$AdjustInventoryBatchRequestDtoFromJson(json);

@override@JsonKey(name: 'direction') final  String direction;
@override@JsonKey(name: 'reason') final  String reason;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'performedAt') final  String? performedAt;
@override@JsonKey(name: 'notes') final  String? notes;

/// Create a copy of AdjustInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AdjustInventoryBatchRequestDtoCopyWith<_AdjustInventoryBatchRequestDto> get copyWith => __$AdjustInventoryBatchRequestDtoCopyWithImpl<_AdjustInventoryBatchRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AdjustInventoryBatchRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AdjustInventoryBatchRequestDto&&(identical(other.direction, direction) || other.direction == direction)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt)&&(identical(other.notes, notes) || other.notes == notes));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,direction,reason,quantity,performedAt,notes);

@override
String toString() {
  return 'AdjustInventoryBatchRequestDto(direction: $direction, reason: $reason, quantity: $quantity, performedAt: $performedAt, notes: $notes)';
}


}

/// @nodoc
abstract mixin class _$AdjustInventoryBatchRequestDtoCopyWith<$Res> implements $AdjustInventoryBatchRequestDtoCopyWith<$Res> {
  factory _$AdjustInventoryBatchRequestDtoCopyWith(_AdjustInventoryBatchRequestDto value, $Res Function(_AdjustInventoryBatchRequestDto) _then) = __$AdjustInventoryBatchRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'direction') String direction,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'performedAt') String? performedAt,@JsonKey(name: 'notes') String? notes
});




}
/// @nodoc
class __$AdjustInventoryBatchRequestDtoCopyWithImpl<$Res>
    implements _$AdjustInventoryBatchRequestDtoCopyWith<$Res> {
  __$AdjustInventoryBatchRequestDtoCopyWithImpl(this._self, this._then);

  final _AdjustInventoryBatchRequestDto _self;
  final $Res Function(_AdjustInventoryBatchRequestDto) _then;

/// Create a copy of AdjustInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? direction = null,Object? reason = null,Object? quantity = null,Object? performedAt = freezed,Object? notes = freezed,}) {
  return _then(_AdjustInventoryBatchRequestDto(
direction: null == direction ? _self.direction : direction // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,performedAt: freezed == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
