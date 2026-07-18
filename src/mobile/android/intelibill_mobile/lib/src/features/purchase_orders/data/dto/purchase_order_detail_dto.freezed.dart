// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'purchase_order_detail_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$PurchaseOrderLineDto {

 String get lineId; String get itemId; String get description; int get expectedQuantity; int get receivedQuantity; int get remainingQuantity; double get unitCost; double get lineTotal;
/// Create a copy of PurchaseOrderLineDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderLineDtoCopyWith<PurchaseOrderLineDto> get copyWith => _$PurchaseOrderLineDtoCopyWithImpl<PurchaseOrderLineDto>(this as PurchaseOrderLineDto, _$identity);

  /// Serializes this PurchaseOrderLineDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderLineDto&&(identical(other.lineId, lineId) || other.lineId == lineId)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.description, description) || other.description == description)&&(identical(other.expectedQuantity, expectedQuantity) || other.expectedQuantity == expectedQuantity)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity)&&(identical(other.remainingQuantity, remainingQuantity) || other.remainingQuantity == remainingQuantity)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost)&&(identical(other.lineTotal, lineTotal) || other.lineTotal == lineTotal));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,lineId,itemId,description,expectedQuantity,receivedQuantity,remainingQuantity,unitCost,lineTotal);

@override
String toString() {
  return 'PurchaseOrderLineDto(lineId: $lineId, itemId: $itemId, description: $description, expectedQuantity: $expectedQuantity, receivedQuantity: $receivedQuantity, remainingQuantity: $remainingQuantity, unitCost: $unitCost, lineTotal: $lineTotal)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderLineDtoCopyWith<$Res>  {
  factory $PurchaseOrderLineDtoCopyWith(PurchaseOrderLineDto value, $Res Function(PurchaseOrderLineDto) _then) = _$PurchaseOrderLineDtoCopyWithImpl;
@useResult
$Res call({
 String lineId, String itemId, String description, int expectedQuantity, int receivedQuantity, int remainingQuantity, double unitCost, double lineTotal
});




}
/// @nodoc
class _$PurchaseOrderLineDtoCopyWithImpl<$Res>
    implements $PurchaseOrderLineDtoCopyWith<$Res> {
  _$PurchaseOrderLineDtoCopyWithImpl(this._self, this._then);

  final PurchaseOrderLineDto _self;
  final $Res Function(PurchaseOrderLineDto) _then;

/// Create a copy of PurchaseOrderLineDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? lineId = null,Object? itemId = null,Object? description = null,Object? expectedQuantity = null,Object? receivedQuantity = null,Object? remainingQuantity = null,Object? unitCost = null,Object? lineTotal = null,}) {
  return _then(_self.copyWith(
lineId: null == lineId ? _self.lineId : lineId // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,expectedQuantity: null == expectedQuantity ? _self.expectedQuantity : expectedQuantity // ignore: cast_nullable_to_non_nullable
as int,receivedQuantity: null == receivedQuantity ? _self.receivedQuantity : receivedQuantity // ignore: cast_nullable_to_non_nullable
as int,remainingQuantity: null == remainingQuantity ? _self.remainingQuantity : remainingQuantity // ignore: cast_nullable_to_non_nullable
as int,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,lineTotal: null == lineTotal ? _self.lineTotal : lineTotal // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [PurchaseOrderLineDto].
extension PurchaseOrderLineDtoPatterns on PurchaseOrderLineDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _PurchaseOrderLineDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _PurchaseOrderLineDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _PurchaseOrderLineDto value)  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderLineDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _PurchaseOrderLineDto value)?  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderLineDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String lineId,  String itemId,  String description,  int expectedQuantity,  int receivedQuantity,  int remainingQuantity,  double unitCost,  double lineTotal)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderLineDto() when $default != null:
return $default(_that.lineId,_that.itemId,_that.description,_that.expectedQuantity,_that.receivedQuantity,_that.remainingQuantity,_that.unitCost,_that.lineTotal);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String lineId,  String itemId,  String description,  int expectedQuantity,  int receivedQuantity,  int remainingQuantity,  double unitCost,  double lineTotal)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderLineDto():
return $default(_that.lineId,_that.itemId,_that.description,_that.expectedQuantity,_that.receivedQuantity,_that.remainingQuantity,_that.unitCost,_that.lineTotal);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String lineId,  String itemId,  String description,  int expectedQuantity,  int receivedQuantity,  int remainingQuantity,  double unitCost,  double lineTotal)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderLineDto() when $default != null:
return $default(_that.lineId,_that.itemId,_that.description,_that.expectedQuantity,_that.receivedQuantity,_that.remainingQuantity,_that.unitCost,_that.lineTotal);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderLineDto implements PurchaseOrderLineDto {
  const _PurchaseOrderLineDto({required this.lineId, required this.itemId, required this.description, required this.expectedQuantity, required this.receivedQuantity, required this.remainingQuantity, required this.unitCost, required this.lineTotal});
  factory _PurchaseOrderLineDto.fromJson(Map<String, dynamic> json) => _$PurchaseOrderLineDtoFromJson(json);

@override final  String lineId;
@override final  String itemId;
@override final  String description;
@override final  int expectedQuantity;
@override final  int receivedQuantity;
@override final  int remainingQuantity;
@override final  double unitCost;
@override final  double lineTotal;

/// Create a copy of PurchaseOrderLineDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PurchaseOrderLineDtoCopyWith<_PurchaseOrderLineDto> get copyWith => __$PurchaseOrderLineDtoCopyWithImpl<_PurchaseOrderLineDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PurchaseOrderLineDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderLineDto&&(identical(other.lineId, lineId) || other.lineId == lineId)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.description, description) || other.description == description)&&(identical(other.expectedQuantity, expectedQuantity) || other.expectedQuantity == expectedQuantity)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity)&&(identical(other.remainingQuantity, remainingQuantity) || other.remainingQuantity == remainingQuantity)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost)&&(identical(other.lineTotal, lineTotal) || other.lineTotal == lineTotal));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,lineId,itemId,description,expectedQuantity,receivedQuantity,remainingQuantity,unitCost,lineTotal);

@override
String toString() {
  return 'PurchaseOrderLineDto(lineId: $lineId, itemId: $itemId, description: $description, expectedQuantity: $expectedQuantity, receivedQuantity: $receivedQuantity, remainingQuantity: $remainingQuantity, unitCost: $unitCost, lineTotal: $lineTotal)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderLineDtoCopyWith<$Res> implements $PurchaseOrderLineDtoCopyWith<$Res> {
  factory _$PurchaseOrderLineDtoCopyWith(_PurchaseOrderLineDto value, $Res Function(_PurchaseOrderLineDto) _then) = __$PurchaseOrderLineDtoCopyWithImpl;
@override @useResult
$Res call({
 String lineId, String itemId, String description, int expectedQuantity, int receivedQuantity, int remainingQuantity, double unitCost, double lineTotal
});




}
/// @nodoc
class __$PurchaseOrderLineDtoCopyWithImpl<$Res>
    implements _$PurchaseOrderLineDtoCopyWith<$Res> {
  __$PurchaseOrderLineDtoCopyWithImpl(this._self, this._then);

  final _PurchaseOrderLineDto _self;
  final $Res Function(_PurchaseOrderLineDto) _then;

/// Create a copy of PurchaseOrderLineDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? lineId = null,Object? itemId = null,Object? description = null,Object? expectedQuantity = null,Object? receivedQuantity = null,Object? remainingQuantity = null,Object? unitCost = null,Object? lineTotal = null,}) {
  return _then(_PurchaseOrderLineDto(
lineId: null == lineId ? _self.lineId : lineId // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,expectedQuantity: null == expectedQuantity ? _self.expectedQuantity : expectedQuantity // ignore: cast_nullable_to_non_nullable
as int,receivedQuantity: null == receivedQuantity ? _self.receivedQuantity : receivedQuantity // ignore: cast_nullable_to_non_nullable
as int,remainingQuantity: null == remainingQuantity ? _self.remainingQuantity : remainingQuantity // ignore: cast_nullable_to_non_nullable
as int,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,lineTotal: null == lineTotal ? _self.lineTotal : lineTotal // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$PurchaseOrderDetailDto {

 String get purchaseOrderId; String get purchaseOrderNumber; String get status; String? get supplierId; String? get orderDate; String? get expectedDeliveryDate; String? get supplierReferenceNumber; String? get notes; List<PurchaseOrderLineDto> get lines; double get expectedTotal; DateTime get createdAt; String? get supplierName; String? get supplierReference; int get receivedQuantity;
/// Create a copy of PurchaseOrderDetailDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderDetailDtoCopyWith<PurchaseOrderDetailDto> get copyWith => _$PurchaseOrderDetailDtoCopyWithImpl<PurchaseOrderDetailDto>(this as PurchaseOrderDetailDto, _$identity);

  /// Serializes this PurchaseOrderDetailDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderDetailDto&&(identical(other.purchaseOrderId, purchaseOrderId) || other.purchaseOrderId == purchaseOrderId)&&(identical(other.purchaseOrderNumber, purchaseOrderNumber) || other.purchaseOrderNumber == purchaseOrderNumber)&&(identical(other.status, status) || other.status == status)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.orderDate, orderDate) || other.orderDate == orderDate)&&(identical(other.expectedDeliveryDate, expectedDeliveryDate) || other.expectedDeliveryDate == expectedDeliveryDate)&&(identical(other.supplierReferenceNumber, supplierReferenceNumber) || other.supplierReferenceNumber == supplierReferenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&const DeepCollectionEquality().equals(other.lines, lines)&&(identical(other.expectedTotal, expectedTotal) || other.expectedTotal == expectedTotal)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,purchaseOrderId,purchaseOrderNumber,status,supplierId,orderDate,expectedDeliveryDate,supplierReferenceNumber,notes,const DeepCollectionEquality().hash(lines),expectedTotal,createdAt,supplierName,supplierReference,receivedQuantity);

@override
String toString() {
  return 'PurchaseOrderDetailDto(purchaseOrderId: $purchaseOrderId, purchaseOrderNumber: $purchaseOrderNumber, status: $status, supplierId: $supplierId, orderDate: $orderDate, expectedDeliveryDate: $expectedDeliveryDate, supplierReferenceNumber: $supplierReferenceNumber, notes: $notes, lines: $lines, expectedTotal: $expectedTotal, createdAt: $createdAt, supplierName: $supplierName, supplierReference: $supplierReference, receivedQuantity: $receivedQuantity)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderDetailDtoCopyWith<$Res>  {
  factory $PurchaseOrderDetailDtoCopyWith(PurchaseOrderDetailDto value, $Res Function(PurchaseOrderDetailDto) _then) = _$PurchaseOrderDetailDtoCopyWithImpl;
@useResult
$Res call({
 String purchaseOrderId, String purchaseOrderNumber, String status, String? supplierId, String? orderDate, String? expectedDeliveryDate, String? supplierReferenceNumber, String? notes, List<PurchaseOrderLineDto> lines, double expectedTotal, DateTime createdAt, String? supplierName, String? supplierReference, int receivedQuantity
});




}
/// @nodoc
class _$PurchaseOrderDetailDtoCopyWithImpl<$Res>
    implements $PurchaseOrderDetailDtoCopyWith<$Res> {
  _$PurchaseOrderDetailDtoCopyWithImpl(this._self, this._then);

  final PurchaseOrderDetailDto _self;
  final $Res Function(PurchaseOrderDetailDto) _then;

/// Create a copy of PurchaseOrderDetailDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? purchaseOrderId = null,Object? purchaseOrderNumber = null,Object? status = null,Object? supplierId = freezed,Object? orderDate = freezed,Object? expectedDeliveryDate = freezed,Object? supplierReferenceNumber = freezed,Object? notes = freezed,Object? lines = null,Object? expectedTotal = null,Object? createdAt = null,Object? supplierName = freezed,Object? supplierReference = freezed,Object? receivedQuantity = null,}) {
  return _then(_self.copyWith(
purchaseOrderId: null == purchaseOrderId ? _self.purchaseOrderId : purchaseOrderId // ignore: cast_nullable_to_non_nullable
as String,purchaseOrderNumber: null == purchaseOrderNumber ? _self.purchaseOrderNumber : purchaseOrderNumber // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,orderDate: freezed == orderDate ? _self.orderDate : orderDate // ignore: cast_nullable_to_non_nullable
as String?,expectedDeliveryDate: freezed == expectedDeliveryDate ? _self.expectedDeliveryDate : expectedDeliveryDate // ignore: cast_nullable_to_non_nullable
as String?,supplierReferenceNumber: freezed == supplierReferenceNumber ? _self.supplierReferenceNumber : supplierReferenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,lines: null == lines ? _self.lines : lines // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderLineDto>,expectedTotal: null == expectedTotal ? _self.expectedTotal : expectedTotal // ignore: cast_nullable_to_non_nullable
as double,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,supplierReference: freezed == supplierReference ? _self.supplierReference : supplierReference // ignore: cast_nullable_to_non_nullable
as String?,receivedQuantity: null == receivedQuantity ? _self.receivedQuantity : receivedQuantity // ignore: cast_nullable_to_non_nullable
as int,
  ));
}

}


/// Adds pattern-matching-related methods to [PurchaseOrderDetailDto].
extension PurchaseOrderDetailDtoPatterns on PurchaseOrderDetailDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _PurchaseOrderDetailDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _PurchaseOrderDetailDto value)  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _PurchaseOrderDetailDto value)?  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  List<PurchaseOrderLineDto> lines,  double expectedTotal,  DateTime createdAt,  String? supplierName,  String? supplierReference,  int receivedQuantity)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto() when $default != null:
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.lines,_that.expectedTotal,_that.createdAt,_that.supplierName,_that.supplierReference,_that.receivedQuantity);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  List<PurchaseOrderLineDto> lines,  double expectedTotal,  DateTime createdAt,  String? supplierName,  String? supplierReference,  int receivedQuantity)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto():
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.lines,_that.expectedTotal,_that.createdAt,_that.supplierName,_that.supplierReference,_that.receivedQuantity);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  List<PurchaseOrderLineDto> lines,  double expectedTotal,  DateTime createdAt,  String? supplierName,  String? supplierReference,  int receivedQuantity)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto() when $default != null:
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.lines,_that.expectedTotal,_that.createdAt,_that.supplierName,_that.supplierReference,_that.receivedQuantity);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderDetailDto implements PurchaseOrderDetailDto {
  const _PurchaseOrderDetailDto({required this.purchaseOrderId, required this.purchaseOrderNumber, required this.status, this.supplierId, this.orderDate, this.expectedDeliveryDate, this.supplierReferenceNumber, this.notes, required final  List<PurchaseOrderLineDto> lines, required this.expectedTotal, required this.createdAt, this.supplierName, this.supplierReference, required this.receivedQuantity}): _lines = lines;
  factory _PurchaseOrderDetailDto.fromJson(Map<String, dynamic> json) => _$PurchaseOrderDetailDtoFromJson(json);

@override final  String purchaseOrderId;
@override final  String purchaseOrderNumber;
@override final  String status;
@override final  String? supplierId;
@override final  String? orderDate;
@override final  String? expectedDeliveryDate;
@override final  String? supplierReferenceNumber;
@override final  String? notes;
 final  List<PurchaseOrderLineDto> _lines;
@override List<PurchaseOrderLineDto> get lines {
  if (_lines is EqualUnmodifiableListView) return _lines;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_lines);
}

@override final  double expectedTotal;
@override final  DateTime createdAt;
@override final  String? supplierName;
@override final  String? supplierReference;
@override final  int receivedQuantity;

/// Create a copy of PurchaseOrderDetailDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PurchaseOrderDetailDtoCopyWith<_PurchaseOrderDetailDto> get copyWith => __$PurchaseOrderDetailDtoCopyWithImpl<_PurchaseOrderDetailDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PurchaseOrderDetailDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderDetailDto&&(identical(other.purchaseOrderId, purchaseOrderId) || other.purchaseOrderId == purchaseOrderId)&&(identical(other.purchaseOrderNumber, purchaseOrderNumber) || other.purchaseOrderNumber == purchaseOrderNumber)&&(identical(other.status, status) || other.status == status)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.orderDate, orderDate) || other.orderDate == orderDate)&&(identical(other.expectedDeliveryDate, expectedDeliveryDate) || other.expectedDeliveryDate == expectedDeliveryDate)&&(identical(other.supplierReferenceNumber, supplierReferenceNumber) || other.supplierReferenceNumber == supplierReferenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&const DeepCollectionEquality().equals(other._lines, _lines)&&(identical(other.expectedTotal, expectedTotal) || other.expectedTotal == expectedTotal)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,purchaseOrderId,purchaseOrderNumber,status,supplierId,orderDate,expectedDeliveryDate,supplierReferenceNumber,notes,const DeepCollectionEquality().hash(_lines),expectedTotal,createdAt,supplierName,supplierReference,receivedQuantity);

@override
String toString() {
  return 'PurchaseOrderDetailDto(purchaseOrderId: $purchaseOrderId, purchaseOrderNumber: $purchaseOrderNumber, status: $status, supplierId: $supplierId, orderDate: $orderDate, expectedDeliveryDate: $expectedDeliveryDate, supplierReferenceNumber: $supplierReferenceNumber, notes: $notes, lines: $lines, expectedTotal: $expectedTotal, createdAt: $createdAt, supplierName: $supplierName, supplierReference: $supplierReference, receivedQuantity: $receivedQuantity)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderDetailDtoCopyWith<$Res> implements $PurchaseOrderDetailDtoCopyWith<$Res> {
  factory _$PurchaseOrderDetailDtoCopyWith(_PurchaseOrderDetailDto value, $Res Function(_PurchaseOrderDetailDto) _then) = __$PurchaseOrderDetailDtoCopyWithImpl;
@override @useResult
$Res call({
 String purchaseOrderId, String purchaseOrderNumber, String status, String? supplierId, String? orderDate, String? expectedDeliveryDate, String? supplierReferenceNumber, String? notes, List<PurchaseOrderLineDto> lines, double expectedTotal, DateTime createdAt, String? supplierName, String? supplierReference, int receivedQuantity
});




}
/// @nodoc
class __$PurchaseOrderDetailDtoCopyWithImpl<$Res>
    implements _$PurchaseOrderDetailDtoCopyWith<$Res> {
  __$PurchaseOrderDetailDtoCopyWithImpl(this._self, this._then);

  final _PurchaseOrderDetailDto _self;
  final $Res Function(_PurchaseOrderDetailDto) _then;

/// Create a copy of PurchaseOrderDetailDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? purchaseOrderId = null,Object? purchaseOrderNumber = null,Object? status = null,Object? supplierId = freezed,Object? orderDate = freezed,Object? expectedDeliveryDate = freezed,Object? supplierReferenceNumber = freezed,Object? notes = freezed,Object? lines = null,Object? expectedTotal = null,Object? createdAt = null,Object? supplierName = freezed,Object? supplierReference = freezed,Object? receivedQuantity = null,}) {
  return _then(_PurchaseOrderDetailDto(
purchaseOrderId: null == purchaseOrderId ? _self.purchaseOrderId : purchaseOrderId // ignore: cast_nullable_to_non_nullable
as String,purchaseOrderNumber: null == purchaseOrderNumber ? _self.purchaseOrderNumber : purchaseOrderNumber // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,orderDate: freezed == orderDate ? _self.orderDate : orderDate // ignore: cast_nullable_to_non_nullable
as String?,expectedDeliveryDate: freezed == expectedDeliveryDate ? _self.expectedDeliveryDate : expectedDeliveryDate // ignore: cast_nullable_to_non_nullable
as String?,supplierReferenceNumber: freezed == supplierReferenceNumber ? _self.supplierReferenceNumber : supplierReferenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,lines: null == lines ? _self._lines : lines // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderLineDto>,expectedTotal: null == expectedTotal ? _self.expectedTotal : expectedTotal // ignore: cast_nullable_to_non_nullable
as double,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,supplierReference: freezed == supplierReference ? _self.supplierReference : supplierReference // ignore: cast_nullable_to_non_nullable
as String?,receivedQuantity: null == receivedQuantity ? _self.receivedQuantity : receivedQuantity // ignore: cast_nullable_to_non_nullable
as int,
  ));
}


}

// dart format on
