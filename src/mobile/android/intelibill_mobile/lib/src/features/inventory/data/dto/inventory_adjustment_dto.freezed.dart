// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'inventory_adjustment_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$InventoryAdjustmentDto {

@JsonKey(name: 'adjustmentId') String get adjustmentId;@JsonKey(name: 'batchId') String get batchId;@JsonKey(name: 'itemId') String get itemId;@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'batchNumber') String get batchNumber;@JsonKey(name: 'direction') String get direction;@JsonKey(name: 'reason') String get reason;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'costImpact') double get costImpact;@JsonKey(name: 'notes') String? get notes;@JsonKey(name: 'performedAt') DateTime get performedAt;@JsonKey(name: 'performedByDisplayName') String get performedByDisplayName;@JsonKey(name: 'isVoided') bool get isVoided;
/// Create a copy of InventoryAdjustmentDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$InventoryAdjustmentDtoCopyWith<InventoryAdjustmentDto> get copyWith => _$InventoryAdjustmentDtoCopyWithImpl<InventoryAdjustmentDto>(this as InventoryAdjustmentDto, _$identity);

  /// Serializes this InventoryAdjustmentDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is InventoryAdjustmentDto&&(identical(other.adjustmentId, adjustmentId) || other.adjustmentId == adjustmentId)&&(identical(other.batchId, batchId) || other.batchId == batchId)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.direction, direction) || other.direction == direction)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.costImpact, costImpact) || other.costImpact == costImpact)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt)&&(identical(other.performedByDisplayName, performedByDisplayName) || other.performedByDisplayName == performedByDisplayName)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,adjustmentId,batchId,itemId,itemName,batchNumber,direction,reason,quantity,costImpact,notes,performedAt,performedByDisplayName,isVoided);

@override
String toString() {
  return 'InventoryAdjustmentDto(adjustmentId: $adjustmentId, batchId: $batchId, itemId: $itemId, itemName: $itemName, batchNumber: $batchNumber, direction: $direction, reason: $reason, quantity: $quantity, costImpact: $costImpact, notes: $notes, performedAt: $performedAt, performedByDisplayName: $performedByDisplayName, isVoided: $isVoided)';
}


}

/// @nodoc
abstract mixin class $InventoryAdjustmentDtoCopyWith<$Res>  {
  factory $InventoryAdjustmentDtoCopyWith(InventoryAdjustmentDto value, $Res Function(InventoryAdjustmentDto) _then) = _$InventoryAdjustmentDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'adjustmentId') String adjustmentId,@JsonKey(name: 'batchId') String batchId,@JsonKey(name: 'itemId') String itemId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'direction') String direction,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'costImpact') double costImpact,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'performedAt') DateTime performedAt,@JsonKey(name: 'performedByDisplayName') String performedByDisplayName,@JsonKey(name: 'isVoided') bool isVoided
});




}
/// @nodoc
class _$InventoryAdjustmentDtoCopyWithImpl<$Res>
    implements $InventoryAdjustmentDtoCopyWith<$Res> {
  _$InventoryAdjustmentDtoCopyWithImpl(this._self, this._then);

  final InventoryAdjustmentDto _self;
  final $Res Function(InventoryAdjustmentDto) _then;

/// Create a copy of InventoryAdjustmentDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? adjustmentId = null,Object? batchId = null,Object? itemId = null,Object? itemName = null,Object? batchNumber = null,Object? direction = null,Object? reason = null,Object? quantity = null,Object? costImpact = null,Object? notes = freezed,Object? performedAt = null,Object? performedByDisplayName = null,Object? isVoided = null,}) {
  return _then(_self.copyWith(
adjustmentId: null == adjustmentId ? _self.adjustmentId : adjustmentId // ignore: cast_nullable_to_non_nullable
as String,batchId: null == batchId ? _self.batchId : batchId // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,direction: null == direction ? _self.direction : direction // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,costImpact: null == costImpact ? _self.costImpact : costImpact // ignore: cast_nullable_to_non_nullable
as double,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,performedAt: null == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as DateTime,performedByDisplayName: null == performedByDisplayName ? _self.performedByDisplayName : performedByDisplayName // ignore: cast_nullable_to_non_nullable
as String,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [InventoryAdjustmentDto].
extension InventoryAdjustmentDtoPatterns on InventoryAdjustmentDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _InventoryAdjustmentDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _InventoryAdjustmentDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _InventoryAdjustmentDto value)  $default,){
final _that = this;
switch (_that) {
case _InventoryAdjustmentDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _InventoryAdjustmentDto value)?  $default,){
final _that = this;
switch (_that) {
case _InventoryAdjustmentDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'adjustmentId')  String adjustmentId, @JsonKey(name: 'batchId')  String batchId, @JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'direction')  String direction, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costImpact')  double costImpact, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'performedAt')  DateTime performedAt, @JsonKey(name: 'performedByDisplayName')  String performedByDisplayName, @JsonKey(name: 'isVoided')  bool isVoided)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _InventoryAdjustmentDto() when $default != null:
return $default(_that.adjustmentId,_that.batchId,_that.itemId,_that.itemName,_that.batchNumber,_that.direction,_that.reason,_that.quantity,_that.costImpact,_that.notes,_that.performedAt,_that.performedByDisplayName,_that.isVoided);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'adjustmentId')  String adjustmentId, @JsonKey(name: 'batchId')  String batchId, @JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'direction')  String direction, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costImpact')  double costImpact, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'performedAt')  DateTime performedAt, @JsonKey(name: 'performedByDisplayName')  String performedByDisplayName, @JsonKey(name: 'isVoided')  bool isVoided)  $default,) {final _that = this;
switch (_that) {
case _InventoryAdjustmentDto():
return $default(_that.adjustmentId,_that.batchId,_that.itemId,_that.itemName,_that.batchNumber,_that.direction,_that.reason,_that.quantity,_that.costImpact,_that.notes,_that.performedAt,_that.performedByDisplayName,_that.isVoided);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'adjustmentId')  String adjustmentId, @JsonKey(name: 'batchId')  String batchId, @JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'direction')  String direction, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costImpact')  double costImpact, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'performedAt')  DateTime performedAt, @JsonKey(name: 'performedByDisplayName')  String performedByDisplayName, @JsonKey(name: 'isVoided')  bool isVoided)?  $default,) {final _that = this;
switch (_that) {
case _InventoryAdjustmentDto() when $default != null:
return $default(_that.adjustmentId,_that.batchId,_that.itemId,_that.itemName,_that.batchNumber,_that.direction,_that.reason,_that.quantity,_that.costImpact,_that.notes,_that.performedAt,_that.performedByDisplayName,_that.isVoided);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _InventoryAdjustmentDto implements InventoryAdjustmentDto {
  const _InventoryAdjustmentDto({@JsonKey(name: 'adjustmentId') required this.adjustmentId, @JsonKey(name: 'batchId') required this.batchId, @JsonKey(name: 'itemId') required this.itemId, @JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'batchNumber') required this.batchNumber, @JsonKey(name: 'direction') required this.direction, @JsonKey(name: 'reason') required this.reason, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'costImpact') required this.costImpact, @JsonKey(name: 'notes') this.notes, @JsonKey(name: 'performedAt') required this.performedAt, @JsonKey(name: 'performedByDisplayName') required this.performedByDisplayName, @JsonKey(name: 'isVoided') this.isVoided = false});
  factory _InventoryAdjustmentDto.fromJson(Map<String, dynamic> json) => _$InventoryAdjustmentDtoFromJson(json);

@override@JsonKey(name: 'adjustmentId') final  String adjustmentId;
@override@JsonKey(name: 'batchId') final  String batchId;
@override@JsonKey(name: 'itemId') final  String itemId;
@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'batchNumber') final  String batchNumber;
@override@JsonKey(name: 'direction') final  String direction;
@override@JsonKey(name: 'reason') final  String reason;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'costImpact') final  double costImpact;
@override@JsonKey(name: 'notes') final  String? notes;
@override@JsonKey(name: 'performedAt') final  DateTime performedAt;
@override@JsonKey(name: 'performedByDisplayName') final  String performedByDisplayName;
@override@JsonKey(name: 'isVoided') final  bool isVoided;

/// Create a copy of InventoryAdjustmentDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$InventoryAdjustmentDtoCopyWith<_InventoryAdjustmentDto> get copyWith => __$InventoryAdjustmentDtoCopyWithImpl<_InventoryAdjustmentDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$InventoryAdjustmentDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _InventoryAdjustmentDto&&(identical(other.adjustmentId, adjustmentId) || other.adjustmentId == adjustmentId)&&(identical(other.batchId, batchId) || other.batchId == batchId)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.direction, direction) || other.direction == direction)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.costImpact, costImpact) || other.costImpact == costImpact)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt)&&(identical(other.performedByDisplayName, performedByDisplayName) || other.performedByDisplayName == performedByDisplayName)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,adjustmentId,batchId,itemId,itemName,batchNumber,direction,reason,quantity,costImpact,notes,performedAt,performedByDisplayName,isVoided);

@override
String toString() {
  return 'InventoryAdjustmentDto(adjustmentId: $adjustmentId, batchId: $batchId, itemId: $itemId, itemName: $itemName, batchNumber: $batchNumber, direction: $direction, reason: $reason, quantity: $quantity, costImpact: $costImpact, notes: $notes, performedAt: $performedAt, performedByDisplayName: $performedByDisplayName, isVoided: $isVoided)';
}


}

/// @nodoc
abstract mixin class _$InventoryAdjustmentDtoCopyWith<$Res> implements $InventoryAdjustmentDtoCopyWith<$Res> {
  factory _$InventoryAdjustmentDtoCopyWith(_InventoryAdjustmentDto value, $Res Function(_InventoryAdjustmentDto) _then) = __$InventoryAdjustmentDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'adjustmentId') String adjustmentId,@JsonKey(name: 'batchId') String batchId,@JsonKey(name: 'itemId') String itemId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'direction') String direction,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'costImpact') double costImpact,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'performedAt') DateTime performedAt,@JsonKey(name: 'performedByDisplayName') String performedByDisplayName,@JsonKey(name: 'isVoided') bool isVoided
});




}
/// @nodoc
class __$InventoryAdjustmentDtoCopyWithImpl<$Res>
    implements _$InventoryAdjustmentDtoCopyWith<$Res> {
  __$InventoryAdjustmentDtoCopyWithImpl(this._self, this._then);

  final _InventoryAdjustmentDto _self;
  final $Res Function(_InventoryAdjustmentDto) _then;

/// Create a copy of InventoryAdjustmentDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? adjustmentId = null,Object? batchId = null,Object? itemId = null,Object? itemName = null,Object? batchNumber = null,Object? direction = null,Object? reason = null,Object? quantity = null,Object? costImpact = null,Object? notes = freezed,Object? performedAt = null,Object? performedByDisplayName = null,Object? isVoided = null,}) {
  return _then(_InventoryAdjustmentDto(
adjustmentId: null == adjustmentId ? _self.adjustmentId : adjustmentId // ignore: cast_nullable_to_non_nullable
as String,batchId: null == batchId ? _self.batchId : batchId // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,direction: null == direction ? _self.direction : direction // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,costImpact: null == costImpact ? _self.costImpact : costImpact // ignore: cast_nullable_to_non_nullable
as double,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,performedAt: null == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as DateTime,performedByDisplayName: null == performedByDisplayName ? _self.performedByDisplayName : performedByDisplayName // ignore: cast_nullable_to_non_nullable
as String,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}

// dart format on
