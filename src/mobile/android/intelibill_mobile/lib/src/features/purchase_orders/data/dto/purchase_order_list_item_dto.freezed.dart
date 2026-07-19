// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'purchase_order_list_item_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$PurchaseOrderListItemDto {

 String get purchaseOrderId; String get purchaseOrderNumber; String get status; String? get supplierName; String? get supplierReference; int get lineCount; int get expectedQuantity; int get receivedQuantity; double get expectedTotal; DateTime get createdAt;
/// Create a copy of PurchaseOrderListItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderListItemDtoCopyWith<PurchaseOrderListItemDto> get copyWith => _$PurchaseOrderListItemDtoCopyWithImpl<PurchaseOrderListItemDto>(this as PurchaseOrderListItemDto, _$identity);

  /// Serializes this PurchaseOrderListItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderListItemDto&&(identical(other.purchaseOrderId, purchaseOrderId) || other.purchaseOrderId == purchaseOrderId)&&(identical(other.purchaseOrderNumber, purchaseOrderNumber) || other.purchaseOrderNumber == purchaseOrderNumber)&&(identical(other.status, status) || other.status == status)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&(identical(other.lineCount, lineCount) || other.lineCount == lineCount)&&(identical(other.expectedQuantity, expectedQuantity) || other.expectedQuantity == expectedQuantity)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity)&&(identical(other.expectedTotal, expectedTotal) || other.expectedTotal == expectedTotal)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,purchaseOrderId,purchaseOrderNumber,status,supplierName,supplierReference,lineCount,expectedQuantity,receivedQuantity,expectedTotal,createdAt);

@override
String toString() {
  return 'PurchaseOrderListItemDto(purchaseOrderId: $purchaseOrderId, purchaseOrderNumber: $purchaseOrderNumber, status: $status, supplierName: $supplierName, supplierReference: $supplierReference, lineCount: $lineCount, expectedQuantity: $expectedQuantity, receivedQuantity: $receivedQuantity, expectedTotal: $expectedTotal, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderListItemDtoCopyWith<$Res>  {
  factory $PurchaseOrderListItemDtoCopyWith(PurchaseOrderListItemDto value, $Res Function(PurchaseOrderListItemDto) _then) = _$PurchaseOrderListItemDtoCopyWithImpl;
@useResult
$Res call({
 String purchaseOrderId, String purchaseOrderNumber, String status, String? supplierName, String? supplierReference, int lineCount, int expectedQuantity, int receivedQuantity, double expectedTotal, DateTime createdAt
});




}
/// @nodoc
class _$PurchaseOrderListItemDtoCopyWithImpl<$Res>
    implements $PurchaseOrderListItemDtoCopyWith<$Res> {
  _$PurchaseOrderListItemDtoCopyWithImpl(this._self, this._then);

  final PurchaseOrderListItemDto _self;
  final $Res Function(PurchaseOrderListItemDto) _then;

/// Create a copy of PurchaseOrderListItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? purchaseOrderId = null,Object? purchaseOrderNumber = null,Object? status = null,Object? supplierName = freezed,Object? supplierReference = freezed,Object? lineCount = null,Object? expectedQuantity = null,Object? receivedQuantity = null,Object? expectedTotal = null,Object? createdAt = null,}) {
  return _then(_self.copyWith(
purchaseOrderId: null == purchaseOrderId ? _self.purchaseOrderId : purchaseOrderId // ignore: cast_nullable_to_non_nullable
as String,purchaseOrderNumber: null == purchaseOrderNumber ? _self.purchaseOrderNumber : purchaseOrderNumber // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,supplierReference: freezed == supplierReference ? _self.supplierReference : supplierReference // ignore: cast_nullable_to_non_nullable
as String?,lineCount: null == lineCount ? _self.lineCount : lineCount // ignore: cast_nullable_to_non_nullable
as int,expectedQuantity: null == expectedQuantity ? _self.expectedQuantity : expectedQuantity // ignore: cast_nullable_to_non_nullable
as int,receivedQuantity: null == receivedQuantity ? _self.receivedQuantity : receivedQuantity // ignore: cast_nullable_to_non_nullable
as int,expectedTotal: null == expectedTotal ? _self.expectedTotal : expectedTotal // ignore: cast_nullable_to_non_nullable
as double,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [PurchaseOrderListItemDto].
extension PurchaseOrderListItemDtoPatterns on PurchaseOrderListItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _PurchaseOrderListItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _PurchaseOrderListItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _PurchaseOrderListItemDto value)  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderListItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _PurchaseOrderListItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderListItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierName,  String? supplierReference,  int lineCount,  int expectedQuantity,  int receivedQuantity,  double expectedTotal,  DateTime createdAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderListItemDto() when $default != null:
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierName,_that.supplierReference,_that.lineCount,_that.expectedQuantity,_that.receivedQuantity,_that.expectedTotal,_that.createdAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierName,  String? supplierReference,  int lineCount,  int expectedQuantity,  int receivedQuantity,  double expectedTotal,  DateTime createdAt)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderListItemDto():
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierName,_that.supplierReference,_that.lineCount,_that.expectedQuantity,_that.receivedQuantity,_that.expectedTotal,_that.createdAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierName,  String? supplierReference,  int lineCount,  int expectedQuantity,  int receivedQuantity,  double expectedTotal,  DateTime createdAt)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderListItemDto() when $default != null:
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierName,_that.supplierReference,_that.lineCount,_that.expectedQuantity,_that.receivedQuantity,_that.expectedTotal,_that.createdAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderListItemDto implements PurchaseOrderListItemDto {
  const _PurchaseOrderListItemDto({required this.purchaseOrderId, required this.purchaseOrderNumber, required this.status, this.supplierName, this.supplierReference, required this.lineCount, required this.expectedQuantity, required this.receivedQuantity, required this.expectedTotal, required this.createdAt});
  factory _PurchaseOrderListItemDto.fromJson(Map<String, dynamic> json) => _$PurchaseOrderListItemDtoFromJson(json);

@override final  String purchaseOrderId;
@override final  String purchaseOrderNumber;
@override final  String status;
@override final  String? supplierName;
@override final  String? supplierReference;
@override final  int lineCount;
@override final  int expectedQuantity;
@override final  int receivedQuantity;
@override final  double expectedTotal;
@override final  DateTime createdAt;

/// Create a copy of PurchaseOrderListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PurchaseOrderListItemDtoCopyWith<_PurchaseOrderListItemDto> get copyWith => __$PurchaseOrderListItemDtoCopyWithImpl<_PurchaseOrderListItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PurchaseOrderListItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderListItemDto&&(identical(other.purchaseOrderId, purchaseOrderId) || other.purchaseOrderId == purchaseOrderId)&&(identical(other.purchaseOrderNumber, purchaseOrderNumber) || other.purchaseOrderNumber == purchaseOrderNumber)&&(identical(other.status, status) || other.status == status)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&(identical(other.lineCount, lineCount) || other.lineCount == lineCount)&&(identical(other.expectedQuantity, expectedQuantity) || other.expectedQuantity == expectedQuantity)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity)&&(identical(other.expectedTotal, expectedTotal) || other.expectedTotal == expectedTotal)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,purchaseOrderId,purchaseOrderNumber,status,supplierName,supplierReference,lineCount,expectedQuantity,receivedQuantity,expectedTotal,createdAt);

@override
String toString() {
  return 'PurchaseOrderListItemDto(purchaseOrderId: $purchaseOrderId, purchaseOrderNumber: $purchaseOrderNumber, status: $status, supplierName: $supplierName, supplierReference: $supplierReference, lineCount: $lineCount, expectedQuantity: $expectedQuantity, receivedQuantity: $receivedQuantity, expectedTotal: $expectedTotal, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderListItemDtoCopyWith<$Res> implements $PurchaseOrderListItemDtoCopyWith<$Res> {
  factory _$PurchaseOrderListItemDtoCopyWith(_PurchaseOrderListItemDto value, $Res Function(_PurchaseOrderListItemDto) _then) = __$PurchaseOrderListItemDtoCopyWithImpl;
@override @useResult
$Res call({
 String purchaseOrderId, String purchaseOrderNumber, String status, String? supplierName, String? supplierReference, int lineCount, int expectedQuantity, int receivedQuantity, double expectedTotal, DateTime createdAt
});




}
/// @nodoc
class __$PurchaseOrderListItemDtoCopyWithImpl<$Res>
    implements _$PurchaseOrderListItemDtoCopyWith<$Res> {
  __$PurchaseOrderListItemDtoCopyWithImpl(this._self, this._then);

  final _PurchaseOrderListItemDto _self;
  final $Res Function(_PurchaseOrderListItemDto) _then;

/// Create a copy of PurchaseOrderListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? purchaseOrderId = null,Object? purchaseOrderNumber = null,Object? status = null,Object? supplierName = freezed,Object? supplierReference = freezed,Object? lineCount = null,Object? expectedQuantity = null,Object? receivedQuantity = null,Object? expectedTotal = null,Object? createdAt = null,}) {
  return _then(_PurchaseOrderListItemDto(
purchaseOrderId: null == purchaseOrderId ? _self.purchaseOrderId : purchaseOrderId // ignore: cast_nullable_to_non_nullable
as String,purchaseOrderNumber: null == purchaseOrderNumber ? _self.purchaseOrderNumber : purchaseOrderNumber // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,supplierReference: freezed == supplierReference ? _self.supplierReference : supplierReference // ignore: cast_nullable_to_non_nullable
as String?,lineCount: null == lineCount ? _self.lineCount : lineCount // ignore: cast_nullable_to_non_nullable
as int,expectedQuantity: null == expectedQuantity ? _self.expectedQuantity : expectedQuantity // ignore: cast_nullable_to_non_nullable
as int,receivedQuantity: null == receivedQuantity ? _self.receivedQuantity : receivedQuantity // ignore: cast_nullable_to_non_nullable
as int,expectedTotal: null == expectedTotal ? _self.expectedTotal : expectedTotal // ignore: cast_nullable_to_non_nullable
as double,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}

// dart format on
