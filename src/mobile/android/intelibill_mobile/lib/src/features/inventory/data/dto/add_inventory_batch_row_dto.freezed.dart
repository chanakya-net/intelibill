// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'add_inventory_batch_row_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AddInventoryBatchRowDto {

@JsonKey(name: 'clientRowId') String get clientRowId;@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'barcode') String get barcode;@JsonKey(name: 'itemDescription') String? get itemDescription;@JsonKey(name: 'uom') String get uom;@JsonKey(name: 'batchNumber') String get batchNumber;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'costPrice') double get costPrice;@JsonKey(name: 'mrp') double get mrp;@JsonKey(name: 'salesPrice') double get salesPrice;@JsonKey(name: 'taxRatePercent') double get taxRatePercent;@JsonKey(name: 'taxIncluded') bool get taxIncluded;@JsonKey(name: 'expiryDate') String? get expiryDate;@JsonKey(name: 'manufacturingDate') String? get manufacturingDate;@JsonKey(name: 'supplierId') String? get supplierId;@JsonKey(name: 'referenceNumber') String? get referenceNumber;@JsonKey(name: 'notes') String? get notes;@JsonKey(name: 'performedAt') String? get performedAt;
/// Create a copy of AddInventoryBatchRowDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryBatchRowDtoCopyWith<AddInventoryBatchRowDto> get copyWith => _$AddInventoryBatchRowDtoCopyWithImpl<AddInventoryBatchRowDto>(this as AddInventoryBatchRowDto, _$identity);

  /// Serializes this AddInventoryBatchRowDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryBatchRowDto&&(identical(other.clientRowId, clientRowId) || other.clientRowId == clientRowId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.itemDescription, itemDescription) || other.itemDescription == itemDescription)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.costPrice, costPrice) || other.costPrice == costPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.manufacturingDate, manufacturingDate) || other.manufacturingDate == manufacturingDate)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,clientRowId,itemName,barcode,itemDescription,uom,batchNumber,quantity,costPrice,mrp,salesPrice,taxRatePercent,taxIncluded,expiryDate,manufacturingDate,supplierId,referenceNumber,notes,performedAt);

@override
String toString() {
  return 'AddInventoryBatchRowDto(clientRowId: $clientRowId, itemName: $itemName, barcode: $barcode, itemDescription: $itemDescription, uom: $uom, batchNumber: $batchNumber, quantity: $quantity, costPrice: $costPrice, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, expiryDate: $expiryDate, manufacturingDate: $manufacturingDate, supplierId: $supplierId, referenceNumber: $referenceNumber, notes: $notes, performedAt: $performedAt)';
}


}

/// @nodoc
abstract mixin class $AddInventoryBatchRowDtoCopyWith<$Res>  {
  factory $AddInventoryBatchRowDtoCopyWith(AddInventoryBatchRowDto value, $Res Function(AddInventoryBatchRowDto) _then) = _$AddInventoryBatchRowDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'clientRowId') String clientRowId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'itemDescription') String? itemDescription,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'costPrice') double costPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded,@JsonKey(name: 'expiryDate') String? expiryDate,@JsonKey(name: 'manufacturingDate') String? manufacturingDate,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'referenceNumber') String? referenceNumber,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'performedAt') String? performedAt
});




}
/// @nodoc
class _$AddInventoryBatchRowDtoCopyWithImpl<$Res>
    implements $AddInventoryBatchRowDtoCopyWith<$Res> {
  _$AddInventoryBatchRowDtoCopyWithImpl(this._self, this._then);

  final AddInventoryBatchRowDto _self;
  final $Res Function(AddInventoryBatchRowDto) _then;

/// Create a copy of AddInventoryBatchRowDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? clientRowId = null,Object? itemName = null,Object? barcode = null,Object? itemDescription = freezed,Object? uom = null,Object? batchNumber = null,Object? quantity = null,Object? costPrice = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? expiryDate = freezed,Object? manufacturingDate = freezed,Object? supplierId = freezed,Object? referenceNumber = freezed,Object? notes = freezed,Object? performedAt = freezed,}) {
  return _then(_self.copyWith(
clientRowId: null == clientRowId ? _self.clientRowId : clientRowId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,itemDescription: freezed == itemDescription ? _self.itemDescription : itemDescription // ignore: cast_nullable_to_non_nullable
as String?,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,costPrice: null == costPrice ? _self.costPrice : costPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as String?,manufacturingDate: freezed == manufacturingDate ? _self.manufacturingDate : manufacturingDate // ignore: cast_nullable_to_non_nullable
as String?,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,performedAt: freezed == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [AddInventoryBatchRowDto].
extension AddInventoryBatchRowDtoPatterns on AddInventoryBatchRowDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryBatchRowDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryBatchRowDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryBatchRowDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchRowDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryBatchRowDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchRowDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'itemDescription')  String? itemDescription, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'expiryDate')  String? expiryDate, @JsonKey(name: 'manufacturingDate')  String? manufacturingDate, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'referenceNumber')  String? referenceNumber, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'performedAt')  String? performedAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryBatchRowDto() when $default != null:
return $default(_that.clientRowId,_that.itemName,_that.barcode,_that.itemDescription,_that.uom,_that.batchNumber,_that.quantity,_that.costPrice,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.expiryDate,_that.manufacturingDate,_that.supplierId,_that.referenceNumber,_that.notes,_that.performedAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'itemDescription')  String? itemDescription, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'expiryDate')  String? expiryDate, @JsonKey(name: 'manufacturingDate')  String? manufacturingDate, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'referenceNumber')  String? referenceNumber, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'performedAt')  String? performedAt)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchRowDto():
return $default(_that.clientRowId,_that.itemName,_that.barcode,_that.itemDescription,_that.uom,_that.batchNumber,_that.quantity,_that.costPrice,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.expiryDate,_that.manufacturingDate,_that.supplierId,_that.referenceNumber,_that.notes,_that.performedAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'itemDescription')  String? itemDescription, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'expiryDate')  String? expiryDate, @JsonKey(name: 'manufacturingDate')  String? manufacturingDate, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'referenceNumber')  String? referenceNumber, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'performedAt')  String? performedAt)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchRowDto() when $default != null:
return $default(_that.clientRowId,_that.itemName,_that.barcode,_that.itemDescription,_that.uom,_that.batchNumber,_that.quantity,_that.costPrice,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.expiryDate,_that.manufacturingDate,_that.supplierId,_that.referenceNumber,_that.notes,_that.performedAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryBatchRowDto implements AddInventoryBatchRowDto {
  const _AddInventoryBatchRowDto({@JsonKey(name: 'clientRowId') required this.clientRowId, @JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'barcode') required this.barcode, @JsonKey(name: 'itemDescription') this.itemDescription, @JsonKey(name: 'uom') required this.uom, @JsonKey(name: 'batchNumber') required this.batchNumber, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'costPrice') required this.costPrice, @JsonKey(name: 'mrp') required this.mrp, @JsonKey(name: 'salesPrice') required this.salesPrice, @JsonKey(name: 'taxRatePercent') required this.taxRatePercent, @JsonKey(name: 'taxIncluded') required this.taxIncluded, @JsonKey(name: 'expiryDate') this.expiryDate, @JsonKey(name: 'manufacturingDate') this.manufacturingDate, @JsonKey(name: 'supplierId') this.supplierId, @JsonKey(name: 'referenceNumber') this.referenceNumber, @JsonKey(name: 'notes') this.notes, @JsonKey(name: 'performedAt') this.performedAt});
  factory _AddInventoryBatchRowDto.fromJson(Map<String, dynamic> json) => _$AddInventoryBatchRowDtoFromJson(json);

@override@JsonKey(name: 'clientRowId') final  String clientRowId;
@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'barcode') final  String barcode;
@override@JsonKey(name: 'itemDescription') final  String? itemDescription;
@override@JsonKey(name: 'uom') final  String uom;
@override@JsonKey(name: 'batchNumber') final  String batchNumber;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'costPrice') final  double costPrice;
@override@JsonKey(name: 'mrp') final  double mrp;
@override@JsonKey(name: 'salesPrice') final  double salesPrice;
@override@JsonKey(name: 'taxRatePercent') final  double taxRatePercent;
@override@JsonKey(name: 'taxIncluded') final  bool taxIncluded;
@override@JsonKey(name: 'expiryDate') final  String? expiryDate;
@override@JsonKey(name: 'manufacturingDate') final  String? manufacturingDate;
@override@JsonKey(name: 'supplierId') final  String? supplierId;
@override@JsonKey(name: 'referenceNumber') final  String? referenceNumber;
@override@JsonKey(name: 'notes') final  String? notes;
@override@JsonKey(name: 'performedAt') final  String? performedAt;

/// Create a copy of AddInventoryBatchRowDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryBatchRowDtoCopyWith<_AddInventoryBatchRowDto> get copyWith => __$AddInventoryBatchRowDtoCopyWithImpl<_AddInventoryBatchRowDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryBatchRowDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryBatchRowDto&&(identical(other.clientRowId, clientRowId) || other.clientRowId == clientRowId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.itemDescription, itemDescription) || other.itemDescription == itemDescription)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.costPrice, costPrice) || other.costPrice == costPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.manufacturingDate, manufacturingDate) || other.manufacturingDate == manufacturingDate)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,clientRowId,itemName,barcode,itemDescription,uom,batchNumber,quantity,costPrice,mrp,salesPrice,taxRatePercent,taxIncluded,expiryDate,manufacturingDate,supplierId,referenceNumber,notes,performedAt);

@override
String toString() {
  return 'AddInventoryBatchRowDto(clientRowId: $clientRowId, itemName: $itemName, barcode: $barcode, itemDescription: $itemDescription, uom: $uom, batchNumber: $batchNumber, quantity: $quantity, costPrice: $costPrice, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, expiryDate: $expiryDate, manufacturingDate: $manufacturingDate, supplierId: $supplierId, referenceNumber: $referenceNumber, notes: $notes, performedAt: $performedAt)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryBatchRowDtoCopyWith<$Res> implements $AddInventoryBatchRowDtoCopyWith<$Res> {
  factory _$AddInventoryBatchRowDtoCopyWith(_AddInventoryBatchRowDto value, $Res Function(_AddInventoryBatchRowDto) _then) = __$AddInventoryBatchRowDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'clientRowId') String clientRowId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'itemDescription') String? itemDescription,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'costPrice') double costPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded,@JsonKey(name: 'expiryDate') String? expiryDate,@JsonKey(name: 'manufacturingDate') String? manufacturingDate,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'referenceNumber') String? referenceNumber,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'performedAt') String? performedAt
});




}
/// @nodoc
class __$AddInventoryBatchRowDtoCopyWithImpl<$Res>
    implements _$AddInventoryBatchRowDtoCopyWith<$Res> {
  __$AddInventoryBatchRowDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryBatchRowDto _self;
  final $Res Function(_AddInventoryBatchRowDto) _then;

/// Create a copy of AddInventoryBatchRowDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? clientRowId = null,Object? itemName = null,Object? barcode = null,Object? itemDescription = freezed,Object? uom = null,Object? batchNumber = null,Object? quantity = null,Object? costPrice = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? expiryDate = freezed,Object? manufacturingDate = freezed,Object? supplierId = freezed,Object? referenceNumber = freezed,Object? notes = freezed,Object? performedAt = freezed,}) {
  return _then(_AddInventoryBatchRowDto(
clientRowId: null == clientRowId ? _self.clientRowId : clientRowId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,itemDescription: freezed == itemDescription ? _self.itemDescription : itemDescription // ignore: cast_nullable_to_non_nullable
as String?,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,costPrice: null == costPrice ? _self.costPrice : costPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as String?,manufacturingDate: freezed == manufacturingDate ? _self.manufacturingDate : manufacturingDate // ignore: cast_nullable_to_non_nullable
as String?,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,performedAt: freezed == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
