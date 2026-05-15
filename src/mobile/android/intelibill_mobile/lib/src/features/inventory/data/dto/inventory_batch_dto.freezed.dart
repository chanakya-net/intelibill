// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'inventory_batch_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$InventoryBatchDto {

@JsonKey(name: 'id') String get id;@JsonKey(name: 'itemId') String get itemId;@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'barcode') String get barcode;@JsonKey(name: 'itemUom') String get itemUom;@JsonKey(name: 'batchNumber') String get batchNumber;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'costPrice') double get costPrice;@JsonKey(name: 'mrp') double get mrp;@JsonKey(name: 'salesPrice') double get salesPrice;@JsonKey(name: 'taxRatePercent') double get taxRatePercent;@JsonKey(name: 'taxIncluded') bool get taxIncluded;@JsonKey(name: 'expiryDate') DateTime? get expiryDate;@JsonKey(name: 'manufacturingDate') DateTime? get manufacturingDate;@JsonKey(name: 'referenceNumber') String? get referenceNumber;@JsonKey(name: 'notes') String? get notes;@JsonKey(name: 'supplierId') String? get supplierId;@JsonKey(name: 'supplierName') String? get supplierName;@JsonKey(name: 'isVoided') bool get isVoided;@JsonKey(name: 'createdAt') DateTime get createdAt;
/// Create a copy of InventoryBatchDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$InventoryBatchDtoCopyWith<InventoryBatchDto> get copyWith => _$InventoryBatchDtoCopyWithImpl<InventoryBatchDto>(this as InventoryBatchDto, _$identity);

  /// Serializes this InventoryBatchDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is InventoryBatchDto&&(identical(other.id, id) || other.id == id)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.itemUom, itemUom) || other.itemUom == itemUom)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.costPrice, costPrice) || other.costPrice == costPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.manufacturingDate, manufacturingDate) || other.manufacturingDate == manufacturingDate)&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,id,itemId,itemName,barcode,itemUom,batchNumber,quantity,costPrice,mrp,salesPrice,taxRatePercent,taxIncluded,expiryDate,manufacturingDate,referenceNumber,notes,supplierId,supplierName,isVoided,createdAt]);

@override
String toString() {
  return 'InventoryBatchDto(id: $id, itemId: $itemId, itemName: $itemName, barcode: $barcode, itemUom: $itemUom, batchNumber: $batchNumber, quantity: $quantity, costPrice: $costPrice, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, expiryDate: $expiryDate, manufacturingDate: $manufacturingDate, referenceNumber: $referenceNumber, notes: $notes, supplierId: $supplierId, supplierName: $supplierName, isVoided: $isVoided, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class $InventoryBatchDtoCopyWith<$Res>  {
  factory $InventoryBatchDtoCopyWith(InventoryBatchDto value, $Res Function(InventoryBatchDto) _then) = _$InventoryBatchDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'itemId') String itemId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'itemUom') String itemUom,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'costPrice') double costPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded,@JsonKey(name: 'expiryDate') DateTime? expiryDate,@JsonKey(name: 'manufacturingDate') DateTime? manufacturingDate,@JsonKey(name: 'referenceNumber') String? referenceNumber,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'supplierName') String? supplierName,@JsonKey(name: 'isVoided') bool isVoided,@JsonKey(name: 'createdAt') DateTime createdAt
});




}
/// @nodoc
class _$InventoryBatchDtoCopyWithImpl<$Res>
    implements $InventoryBatchDtoCopyWith<$Res> {
  _$InventoryBatchDtoCopyWithImpl(this._self, this._then);

  final InventoryBatchDto _self;
  final $Res Function(InventoryBatchDto) _then;

/// Create a copy of InventoryBatchDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? itemId = null,Object? itemName = null,Object? barcode = null,Object? itemUom = null,Object? batchNumber = null,Object? quantity = null,Object? costPrice = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? expiryDate = freezed,Object? manufacturingDate = freezed,Object? referenceNumber = freezed,Object? notes = freezed,Object? supplierId = freezed,Object? supplierName = freezed,Object? isVoided = null,Object? createdAt = null,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,itemUom: null == itemUom ? _self.itemUom : itemUom // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,costPrice: null == costPrice ? _self.costPrice : costPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as DateTime?,manufacturingDate: freezed == manufacturingDate ? _self.manufacturingDate : manufacturingDate // ignore: cast_nullable_to_non_nullable
as DateTime?,referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [InventoryBatchDto].
extension InventoryBatchDtoPatterns on InventoryBatchDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _InventoryBatchDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _InventoryBatchDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _InventoryBatchDto value)  $default,){
final _that = this;
switch (_that) {
case _InventoryBatchDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _InventoryBatchDto value)?  $default,){
final _that = this;
switch (_that) {
case _InventoryBatchDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'itemUom')  String itemUom, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'expiryDate')  DateTime? expiryDate, @JsonKey(name: 'manufacturingDate')  DateTime? manufacturingDate, @JsonKey(name: 'referenceNumber')  String? referenceNumber, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'supplierName')  String? supplierName, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'createdAt')  DateTime createdAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _InventoryBatchDto() when $default != null:
return $default(_that.id,_that.itemId,_that.itemName,_that.barcode,_that.itemUom,_that.batchNumber,_that.quantity,_that.costPrice,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.expiryDate,_that.manufacturingDate,_that.referenceNumber,_that.notes,_that.supplierId,_that.supplierName,_that.isVoided,_that.createdAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'itemUom')  String itemUom, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'expiryDate')  DateTime? expiryDate, @JsonKey(name: 'manufacturingDate')  DateTime? manufacturingDate, @JsonKey(name: 'referenceNumber')  String? referenceNumber, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'supplierName')  String? supplierName, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'createdAt')  DateTime createdAt)  $default,) {final _that = this;
switch (_that) {
case _InventoryBatchDto():
return $default(_that.id,_that.itemId,_that.itemName,_that.barcode,_that.itemUom,_that.batchNumber,_that.quantity,_that.costPrice,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.expiryDate,_that.manufacturingDate,_that.referenceNumber,_that.notes,_that.supplierId,_that.supplierName,_that.isVoided,_that.createdAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'itemUom')  String itemUom, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'expiryDate')  DateTime? expiryDate, @JsonKey(name: 'manufacturingDate')  DateTime? manufacturingDate, @JsonKey(name: 'referenceNumber')  String? referenceNumber, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'supplierName')  String? supplierName, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'createdAt')  DateTime createdAt)?  $default,) {final _that = this;
switch (_that) {
case _InventoryBatchDto() when $default != null:
return $default(_that.id,_that.itemId,_that.itemName,_that.barcode,_that.itemUom,_that.batchNumber,_that.quantity,_that.costPrice,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.expiryDate,_that.manufacturingDate,_that.referenceNumber,_that.notes,_that.supplierId,_that.supplierName,_that.isVoided,_that.createdAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _InventoryBatchDto implements InventoryBatchDto {
  const _InventoryBatchDto({@JsonKey(name: 'id') required this.id, @JsonKey(name: 'itemId') required this.itemId, @JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'barcode') required this.barcode, @JsonKey(name: 'itemUom') this.itemUom = '', @JsonKey(name: 'batchNumber') required this.batchNumber, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'costPrice') required this.costPrice, @JsonKey(name: 'mrp') required this.mrp, @JsonKey(name: 'salesPrice') required this.salesPrice, @JsonKey(name: 'taxRatePercent') this.taxRatePercent = 0.0, @JsonKey(name: 'taxIncluded') this.taxIncluded = false, @JsonKey(name: 'expiryDate') this.expiryDate, @JsonKey(name: 'manufacturingDate') this.manufacturingDate, @JsonKey(name: 'referenceNumber') this.referenceNumber, @JsonKey(name: 'notes') this.notes, @JsonKey(name: 'supplierId') this.supplierId, @JsonKey(name: 'supplierName') this.supplierName, @JsonKey(name: 'isVoided') this.isVoided = false, @JsonKey(name: 'createdAt') required this.createdAt});
  factory _InventoryBatchDto.fromJson(Map<String, dynamic> json) => _$InventoryBatchDtoFromJson(json);

@override@JsonKey(name: 'id') final  String id;
@override@JsonKey(name: 'itemId') final  String itemId;
@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'barcode') final  String barcode;
@override@JsonKey(name: 'itemUom') final  String itemUom;
@override@JsonKey(name: 'batchNumber') final  String batchNumber;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'costPrice') final  double costPrice;
@override@JsonKey(name: 'mrp') final  double mrp;
@override@JsonKey(name: 'salesPrice') final  double salesPrice;
@override@JsonKey(name: 'taxRatePercent') final  double taxRatePercent;
@override@JsonKey(name: 'taxIncluded') final  bool taxIncluded;
@override@JsonKey(name: 'expiryDate') final  DateTime? expiryDate;
@override@JsonKey(name: 'manufacturingDate') final  DateTime? manufacturingDate;
@override@JsonKey(name: 'referenceNumber') final  String? referenceNumber;
@override@JsonKey(name: 'notes') final  String? notes;
@override@JsonKey(name: 'supplierId') final  String? supplierId;
@override@JsonKey(name: 'supplierName') final  String? supplierName;
@override@JsonKey(name: 'isVoided') final  bool isVoided;
@override@JsonKey(name: 'createdAt') final  DateTime createdAt;

/// Create a copy of InventoryBatchDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$InventoryBatchDtoCopyWith<_InventoryBatchDto> get copyWith => __$InventoryBatchDtoCopyWithImpl<_InventoryBatchDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$InventoryBatchDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _InventoryBatchDto&&(identical(other.id, id) || other.id == id)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.itemUom, itemUom) || other.itemUom == itemUom)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.costPrice, costPrice) || other.costPrice == costPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.manufacturingDate, manufacturingDate) || other.manufacturingDate == manufacturingDate)&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,id,itemId,itemName,barcode,itemUom,batchNumber,quantity,costPrice,mrp,salesPrice,taxRatePercent,taxIncluded,expiryDate,manufacturingDate,referenceNumber,notes,supplierId,supplierName,isVoided,createdAt]);

@override
String toString() {
  return 'InventoryBatchDto(id: $id, itemId: $itemId, itemName: $itemName, barcode: $barcode, itemUom: $itemUom, batchNumber: $batchNumber, quantity: $quantity, costPrice: $costPrice, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, expiryDate: $expiryDate, manufacturingDate: $manufacturingDate, referenceNumber: $referenceNumber, notes: $notes, supplierId: $supplierId, supplierName: $supplierName, isVoided: $isVoided, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class _$InventoryBatchDtoCopyWith<$Res> implements $InventoryBatchDtoCopyWith<$Res> {
  factory _$InventoryBatchDtoCopyWith(_InventoryBatchDto value, $Res Function(_InventoryBatchDto) _then) = __$InventoryBatchDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'itemId') String itemId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'itemUom') String itemUom,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'costPrice') double costPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded,@JsonKey(name: 'expiryDate') DateTime? expiryDate,@JsonKey(name: 'manufacturingDate') DateTime? manufacturingDate,@JsonKey(name: 'referenceNumber') String? referenceNumber,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'supplierName') String? supplierName,@JsonKey(name: 'isVoided') bool isVoided,@JsonKey(name: 'createdAt') DateTime createdAt
});




}
/// @nodoc
class __$InventoryBatchDtoCopyWithImpl<$Res>
    implements _$InventoryBatchDtoCopyWith<$Res> {
  __$InventoryBatchDtoCopyWithImpl(this._self, this._then);

  final _InventoryBatchDto _self;
  final $Res Function(_InventoryBatchDto) _then;

/// Create a copy of InventoryBatchDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? itemId = null,Object? itemName = null,Object? barcode = null,Object? itemUom = null,Object? batchNumber = null,Object? quantity = null,Object? costPrice = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? expiryDate = freezed,Object? manufacturingDate = freezed,Object? referenceNumber = freezed,Object? notes = freezed,Object? supplierId = freezed,Object? supplierName = freezed,Object? isVoided = null,Object? createdAt = null,}) {
  return _then(_InventoryBatchDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,itemUom: null == itemUom ? _self.itemUom : itemUom // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,costPrice: null == costPrice ? _self.costPrice : costPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as DateTime?,manufacturingDate: freezed == manufacturingDate ? _self.manufacturingDate : manufacturingDate // ignore: cast_nullable_to_non_nullable
as DateTime?,referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}

// dart format on
