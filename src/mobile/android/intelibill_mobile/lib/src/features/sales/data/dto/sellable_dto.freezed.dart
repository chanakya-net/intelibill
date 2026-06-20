// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'sellable_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SellableDto {

@JsonKey(name: 'kind') String get kind;@JsonKey(name: 'inventoryBatchId') String? get inventoryBatchId;@JsonKey(name: 'barcode') String? get barcode;@JsonKey(name: 'itemName') String? get itemName;@JsonKey(name: 'batchNumber') String? get batchNumber;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'salesPrice') double get salesPrice;@JsonKey(name: 'mrp') double get mrp;@JsonKey(name: 'taxRatePercent') double get taxRatePercent;@JsonKey(name: 'taxIncluded') bool get taxIncluded;@JsonKey(name: 'purchaseTaxIncluded') bool get purchaseTaxIncluded;@JsonKey(name: 'expiryDate') DateTime? get expiryDate;@JsonKey(name: 'serviceId') String? get serviceId;@JsonKey(name: 'code') String? get code;@JsonKey(name: 'name') String? get name;@JsonKey(name: 'description') String? get description;@JsonKey(name: 'price') double get price;@JsonKey(name: 'hsnCode') String? get hsnCode;
/// Create a copy of SellableDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SellableDtoCopyWith<SellableDto> get copyWith => _$SellableDtoCopyWithImpl<SellableDto>(this as SellableDto, _$identity);

  /// Serializes this SellableDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SellableDto&&(identical(other.kind, kind) || other.kind == kind)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.purchaseTaxIncluded, purchaseTaxIncluded) || other.purchaseTaxIncluded == purchaseTaxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.serviceId, serviceId) || other.serviceId == serviceId)&&(identical(other.code, code) || other.code == code)&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.price, price) || other.price == price)&&(identical(other.hsnCode, hsnCode) || other.hsnCode == hsnCode));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,kind,inventoryBatchId,barcode,itemName,batchNumber,quantity,salesPrice,mrp,taxRatePercent,taxIncluded,purchaseTaxIncluded,expiryDate,serviceId,code,name,description,price,hsnCode);

@override
String toString() {
  return 'SellableDto(kind: $kind, inventoryBatchId: $inventoryBatchId, barcode: $barcode, itemName: $itemName, batchNumber: $batchNumber, quantity: $quantity, salesPrice: $salesPrice, mrp: $mrp, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, purchaseTaxIncluded: $purchaseTaxIncluded, expiryDate: $expiryDate, serviceId: $serviceId, code: $code, name: $name, description: $description, price: $price, hsnCode: $hsnCode)';
}


}

/// @nodoc
abstract mixin class $SellableDtoCopyWith<$Res>  {
  factory $SellableDtoCopyWith(SellableDto value, $Res Function(SellableDto) _then) = _$SellableDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'kind') String kind,@JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,@JsonKey(name: 'barcode') String? barcode,@JsonKey(name: 'itemName') String? itemName,@JsonKey(name: 'batchNumber') String? batchNumber,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded,@JsonKey(name: 'purchaseTaxIncluded') bool purchaseTaxIncluded,@JsonKey(name: 'expiryDate') DateTime? expiryDate,@JsonKey(name: 'serviceId') String? serviceId,@JsonKey(name: 'code') String? code,@JsonKey(name: 'name') String? name,@JsonKey(name: 'description') String? description,@JsonKey(name: 'price') double price,@JsonKey(name: 'hsnCode') String? hsnCode
});




}
/// @nodoc
class _$SellableDtoCopyWithImpl<$Res>
    implements $SellableDtoCopyWith<$Res> {
  _$SellableDtoCopyWithImpl(this._self, this._then);

  final SellableDto _self;
  final $Res Function(SellableDto) _then;

/// Create a copy of SellableDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? kind = null,Object? inventoryBatchId = freezed,Object? barcode = freezed,Object? itemName = freezed,Object? batchNumber = freezed,Object? quantity = null,Object? salesPrice = null,Object? mrp = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? purchaseTaxIncluded = null,Object? expiryDate = freezed,Object? serviceId = freezed,Object? code = freezed,Object? name = freezed,Object? description = freezed,Object? price = null,Object? hsnCode = freezed,}) {
  return _then(_self.copyWith(
kind: null == kind ? _self.kind : kind // ignore: cast_nullable_to_non_nullable
as String,inventoryBatchId: freezed == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String?,barcode: freezed == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String?,itemName: freezed == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String?,batchNumber: freezed == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String?,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,purchaseTaxIncluded: null == purchaseTaxIncluded ? _self.purchaseTaxIncluded : purchaseTaxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as DateTime?,serviceId: freezed == serviceId ? _self.serviceId : serviceId // ignore: cast_nullable_to_non_nullable
as String?,code: freezed == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String?,name: freezed == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String?,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,price: null == price ? _self.price : price // ignore: cast_nullable_to_non_nullable
as double,hsnCode: freezed == hsnCode ? _self.hsnCode : hsnCode // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [SellableDto].
extension SellableDtoPatterns on SellableDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SellableDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SellableDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SellableDto value)  $default,){
final _that = this;
switch (_that) {
case _SellableDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SellableDto value)?  $default,){
final _that = this;
switch (_that) {
case _SellableDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'kind')  String kind, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'barcode')  String? barcode, @JsonKey(name: 'itemName')  String? itemName, @JsonKey(name: 'batchNumber')  String? batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'purchaseTaxIncluded')  bool purchaseTaxIncluded, @JsonKey(name: 'expiryDate')  DateTime? expiryDate, @JsonKey(name: 'serviceId')  String? serviceId, @JsonKey(name: 'code')  String? code, @JsonKey(name: 'name')  String? name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'price')  double price, @JsonKey(name: 'hsnCode')  String? hsnCode)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SellableDto() when $default != null:
return $default(_that.kind,_that.inventoryBatchId,_that.barcode,_that.itemName,_that.batchNumber,_that.quantity,_that.salesPrice,_that.mrp,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded,_that.expiryDate,_that.serviceId,_that.code,_that.name,_that.description,_that.price,_that.hsnCode);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'kind')  String kind, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'barcode')  String? barcode, @JsonKey(name: 'itemName')  String? itemName, @JsonKey(name: 'batchNumber')  String? batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'purchaseTaxIncluded')  bool purchaseTaxIncluded, @JsonKey(name: 'expiryDate')  DateTime? expiryDate, @JsonKey(name: 'serviceId')  String? serviceId, @JsonKey(name: 'code')  String? code, @JsonKey(name: 'name')  String? name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'price')  double price, @JsonKey(name: 'hsnCode')  String? hsnCode)  $default,) {final _that = this;
switch (_that) {
case _SellableDto():
return $default(_that.kind,_that.inventoryBatchId,_that.barcode,_that.itemName,_that.batchNumber,_that.quantity,_that.salesPrice,_that.mrp,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded,_that.expiryDate,_that.serviceId,_that.code,_that.name,_that.description,_that.price,_that.hsnCode);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'kind')  String kind, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'barcode')  String? barcode, @JsonKey(name: 'itemName')  String? itemName, @JsonKey(name: 'batchNumber')  String? batchNumber, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded, @JsonKey(name: 'purchaseTaxIncluded')  bool purchaseTaxIncluded, @JsonKey(name: 'expiryDate')  DateTime? expiryDate, @JsonKey(name: 'serviceId')  String? serviceId, @JsonKey(name: 'code')  String? code, @JsonKey(name: 'name')  String? name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'price')  double price, @JsonKey(name: 'hsnCode')  String? hsnCode)?  $default,) {final _that = this;
switch (_that) {
case _SellableDto() when $default != null:
return $default(_that.kind,_that.inventoryBatchId,_that.barcode,_that.itemName,_that.batchNumber,_that.quantity,_that.salesPrice,_that.mrp,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded,_that.expiryDate,_that.serviceId,_that.code,_that.name,_that.description,_that.price,_that.hsnCode);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SellableDto implements SellableDto {
  const _SellableDto({@JsonKey(name: 'kind') required this.kind, @JsonKey(name: 'inventoryBatchId') this.inventoryBatchId, @JsonKey(name: 'barcode') this.barcode, @JsonKey(name: 'itemName') this.itemName, @JsonKey(name: 'batchNumber') this.batchNumber, @JsonKey(name: 'quantity') this.quantity = 0.0, @JsonKey(name: 'salesPrice') this.salesPrice = 0.0, @JsonKey(name: 'mrp') this.mrp = 0.0, @JsonKey(name: 'taxRatePercent') this.taxRatePercent = 0.0, @JsonKey(name: 'taxIncluded') this.taxIncluded = false, @JsonKey(name: 'purchaseTaxIncluded') this.purchaseTaxIncluded = false, @JsonKey(name: 'expiryDate') this.expiryDate, @JsonKey(name: 'serviceId') this.serviceId, @JsonKey(name: 'code') this.code, @JsonKey(name: 'name') this.name, @JsonKey(name: 'description') this.description, @JsonKey(name: 'price') this.price = 0.0, @JsonKey(name: 'hsnCode') this.hsnCode});
  factory _SellableDto.fromJson(Map<String, dynamic> json) => _$SellableDtoFromJson(json);

@override@JsonKey(name: 'kind') final  String kind;
@override@JsonKey(name: 'inventoryBatchId') final  String? inventoryBatchId;
@override@JsonKey(name: 'barcode') final  String? barcode;
@override@JsonKey(name: 'itemName') final  String? itemName;
@override@JsonKey(name: 'batchNumber') final  String? batchNumber;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'salesPrice') final  double salesPrice;
@override@JsonKey(name: 'mrp') final  double mrp;
@override@JsonKey(name: 'taxRatePercent') final  double taxRatePercent;
@override@JsonKey(name: 'taxIncluded') final  bool taxIncluded;
@override@JsonKey(name: 'purchaseTaxIncluded') final  bool purchaseTaxIncluded;
@override@JsonKey(name: 'expiryDate') final  DateTime? expiryDate;
@override@JsonKey(name: 'serviceId') final  String? serviceId;
@override@JsonKey(name: 'code') final  String? code;
@override@JsonKey(name: 'name') final  String? name;
@override@JsonKey(name: 'description') final  String? description;
@override@JsonKey(name: 'price') final  double price;
@override@JsonKey(name: 'hsnCode') final  String? hsnCode;

/// Create a copy of SellableDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SellableDtoCopyWith<_SellableDto> get copyWith => __$SellableDtoCopyWithImpl<_SellableDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SellableDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SellableDto&&(identical(other.kind, kind) || other.kind == kind)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.purchaseTaxIncluded, purchaseTaxIncluded) || other.purchaseTaxIncluded == purchaseTaxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.serviceId, serviceId) || other.serviceId == serviceId)&&(identical(other.code, code) || other.code == code)&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.price, price) || other.price == price)&&(identical(other.hsnCode, hsnCode) || other.hsnCode == hsnCode));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,kind,inventoryBatchId,barcode,itemName,batchNumber,quantity,salesPrice,mrp,taxRatePercent,taxIncluded,purchaseTaxIncluded,expiryDate,serviceId,code,name,description,price,hsnCode);

@override
String toString() {
  return 'SellableDto(kind: $kind, inventoryBatchId: $inventoryBatchId, barcode: $barcode, itemName: $itemName, batchNumber: $batchNumber, quantity: $quantity, salesPrice: $salesPrice, mrp: $mrp, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, purchaseTaxIncluded: $purchaseTaxIncluded, expiryDate: $expiryDate, serviceId: $serviceId, code: $code, name: $name, description: $description, price: $price, hsnCode: $hsnCode)';
}


}

/// @nodoc
abstract mixin class _$SellableDtoCopyWith<$Res> implements $SellableDtoCopyWith<$Res> {
  factory _$SellableDtoCopyWith(_SellableDto value, $Res Function(_SellableDto) _then) = __$SellableDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'kind') String kind,@JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,@JsonKey(name: 'barcode') String? barcode,@JsonKey(name: 'itemName') String? itemName,@JsonKey(name: 'batchNumber') String? batchNumber,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded,@JsonKey(name: 'purchaseTaxIncluded') bool purchaseTaxIncluded,@JsonKey(name: 'expiryDate') DateTime? expiryDate,@JsonKey(name: 'serviceId') String? serviceId,@JsonKey(name: 'code') String? code,@JsonKey(name: 'name') String? name,@JsonKey(name: 'description') String? description,@JsonKey(name: 'price') double price,@JsonKey(name: 'hsnCode') String? hsnCode
});




}
/// @nodoc
class __$SellableDtoCopyWithImpl<$Res>
    implements _$SellableDtoCopyWith<$Res> {
  __$SellableDtoCopyWithImpl(this._self, this._then);

  final _SellableDto _self;
  final $Res Function(_SellableDto) _then;

/// Create a copy of SellableDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? kind = null,Object? inventoryBatchId = freezed,Object? barcode = freezed,Object? itemName = freezed,Object? batchNumber = freezed,Object? quantity = null,Object? salesPrice = null,Object? mrp = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? purchaseTaxIncluded = null,Object? expiryDate = freezed,Object? serviceId = freezed,Object? code = freezed,Object? name = freezed,Object? description = freezed,Object? price = null,Object? hsnCode = freezed,}) {
  return _then(_SellableDto(
kind: null == kind ? _self.kind : kind // ignore: cast_nullable_to_non_nullable
as String,inventoryBatchId: freezed == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String?,barcode: freezed == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String?,itemName: freezed == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String?,batchNumber: freezed == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String?,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,purchaseTaxIncluded: null == purchaseTaxIncluded ? _self.purchaseTaxIncluded : purchaseTaxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as DateTime?,serviceId: freezed == serviceId ? _self.serviceId : serviceId // ignore: cast_nullable_to_non_nullable
as String?,code: freezed == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String?,name: freezed == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String?,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,price: null == price ? _self.price : price // ignore: cast_nullable_to_non_nullable
as double,hsnCode: freezed == hsnCode ? _self.hsnCode : hsnCode // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
