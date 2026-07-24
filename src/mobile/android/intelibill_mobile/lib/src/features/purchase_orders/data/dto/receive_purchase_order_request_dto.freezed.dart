// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'receive_purchase_order_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ReceivePurchaseOrderLineRequestDto {

 String get purchaseOrderLineId;@JsonKey(toJson: _trim) String get barcode;@JsonKey(toJson: _trim) String get batchNumber;@JsonKey(toJson: _quantityToJson) double get quantity; double get totalPurchaseCost;@JsonKey(includeToJson: false) double get unitCost; double get mrp; double get salesPrice; double get taxRatePercent; bool get taxIncluded; bool get purchaseTaxIncluded;@JsonKey(toJson: _dateOnlyToJson) DateTime? get expiryDate;@JsonKey(toJson: _dateOnlyToJson) DateTime? get manufacturingDate;
/// Create a copy of ReceivePurchaseOrderLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ReceivePurchaseOrderLineRequestDtoCopyWith<ReceivePurchaseOrderLineRequestDto> get copyWith => _$ReceivePurchaseOrderLineRequestDtoCopyWithImpl<ReceivePurchaseOrderLineRequestDto>(this as ReceivePurchaseOrderLineRequestDto, _$identity);

  /// Serializes this ReceivePurchaseOrderLineRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ReceivePurchaseOrderLineRequestDto&&(identical(other.purchaseOrderLineId, purchaseOrderLineId) || other.purchaseOrderLineId == purchaseOrderLineId)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.totalPurchaseCost, totalPurchaseCost) || other.totalPurchaseCost == totalPurchaseCost)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.purchaseTaxIncluded, purchaseTaxIncluded) || other.purchaseTaxIncluded == purchaseTaxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.manufacturingDate, manufacturingDate) || other.manufacturingDate == manufacturingDate));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,purchaseOrderLineId,barcode,batchNumber,quantity,totalPurchaseCost,unitCost,mrp,salesPrice,taxRatePercent,taxIncluded,purchaseTaxIncluded,expiryDate,manufacturingDate);

@override
String toString() {
  return 'ReceivePurchaseOrderLineRequestDto(purchaseOrderLineId: $purchaseOrderLineId, barcode: $barcode, batchNumber: $batchNumber, quantity: $quantity, totalPurchaseCost: $totalPurchaseCost, unitCost: $unitCost, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, purchaseTaxIncluded: $purchaseTaxIncluded, expiryDate: $expiryDate, manufacturingDate: $manufacturingDate)';
}


}

/// @nodoc
abstract mixin class $ReceivePurchaseOrderLineRequestDtoCopyWith<$Res>  {
  factory $ReceivePurchaseOrderLineRequestDtoCopyWith(ReceivePurchaseOrderLineRequestDto value, $Res Function(ReceivePurchaseOrderLineRequestDto) _then) = _$ReceivePurchaseOrderLineRequestDtoCopyWithImpl;
@useResult
$Res call({
 String purchaseOrderLineId,@JsonKey(toJson: _trim) String barcode,@JsonKey(toJson: _trim) String batchNumber,@JsonKey(toJson: _quantityToJson) double quantity, double totalPurchaseCost,@JsonKey(includeToJson: false) double unitCost, double mrp, double salesPrice, double taxRatePercent, bool taxIncluded, bool purchaseTaxIncluded,@JsonKey(toJson: _dateOnlyToJson) DateTime? expiryDate,@JsonKey(toJson: _dateOnlyToJson) DateTime? manufacturingDate
});




}
/// @nodoc
class _$ReceivePurchaseOrderLineRequestDtoCopyWithImpl<$Res>
    implements $ReceivePurchaseOrderLineRequestDtoCopyWith<$Res> {
  _$ReceivePurchaseOrderLineRequestDtoCopyWithImpl(this._self, this._then);

  final ReceivePurchaseOrderLineRequestDto _self;
  final $Res Function(ReceivePurchaseOrderLineRequestDto) _then;

/// Create a copy of ReceivePurchaseOrderLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? purchaseOrderLineId = null,Object? barcode = null,Object? batchNumber = null,Object? quantity = null,Object? totalPurchaseCost = null,Object? unitCost = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? purchaseTaxIncluded = null,Object? expiryDate = freezed,Object? manufacturingDate = freezed,}) {
  return _then(_self.copyWith(
purchaseOrderLineId: null == purchaseOrderLineId ? _self.purchaseOrderLineId : purchaseOrderLineId // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,totalPurchaseCost: null == totalPurchaseCost ? _self.totalPurchaseCost : totalPurchaseCost // ignore: cast_nullable_to_non_nullable
as double,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,purchaseTaxIncluded: null == purchaseTaxIncluded ? _self.purchaseTaxIncluded : purchaseTaxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as DateTime?,manufacturingDate: freezed == manufacturingDate ? _self.manufacturingDate : manufacturingDate // ignore: cast_nullable_to_non_nullable
as DateTime?,
  ));
}

}


/// Adds pattern-matching-related methods to [ReceivePurchaseOrderLineRequestDto].
extension ReceivePurchaseOrderLineRequestDtoPatterns on ReceivePurchaseOrderLineRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ReceivePurchaseOrderLineRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ReceivePurchaseOrderLineRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ReceivePurchaseOrderLineRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _ReceivePurchaseOrderLineRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ReceivePurchaseOrderLineRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _ReceivePurchaseOrderLineRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String purchaseOrderLineId, @JsonKey(toJson: _trim)  String barcode, @JsonKey(toJson: _trim)  String batchNumber, @JsonKey(toJson: _quantityToJson)  double quantity,  double totalPurchaseCost, @JsonKey(includeToJson: false)  double unitCost,  double mrp,  double salesPrice,  double taxRatePercent,  bool taxIncluded,  bool purchaseTaxIncluded, @JsonKey(toJson: _dateOnlyToJson)  DateTime? expiryDate, @JsonKey(toJson: _dateOnlyToJson)  DateTime? manufacturingDate)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ReceivePurchaseOrderLineRequestDto() when $default != null:
return $default(_that.purchaseOrderLineId,_that.barcode,_that.batchNumber,_that.quantity,_that.totalPurchaseCost,_that.unitCost,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded,_that.expiryDate,_that.manufacturingDate);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String purchaseOrderLineId, @JsonKey(toJson: _trim)  String barcode, @JsonKey(toJson: _trim)  String batchNumber, @JsonKey(toJson: _quantityToJson)  double quantity,  double totalPurchaseCost, @JsonKey(includeToJson: false)  double unitCost,  double mrp,  double salesPrice,  double taxRatePercent,  bool taxIncluded,  bool purchaseTaxIncluded, @JsonKey(toJson: _dateOnlyToJson)  DateTime? expiryDate, @JsonKey(toJson: _dateOnlyToJson)  DateTime? manufacturingDate)  $default,) {final _that = this;
switch (_that) {
case _ReceivePurchaseOrderLineRequestDto():
return $default(_that.purchaseOrderLineId,_that.barcode,_that.batchNumber,_that.quantity,_that.totalPurchaseCost,_that.unitCost,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded,_that.expiryDate,_that.manufacturingDate);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String purchaseOrderLineId, @JsonKey(toJson: _trim)  String barcode, @JsonKey(toJson: _trim)  String batchNumber, @JsonKey(toJson: _quantityToJson)  double quantity,  double totalPurchaseCost, @JsonKey(includeToJson: false)  double unitCost,  double mrp,  double salesPrice,  double taxRatePercent,  bool taxIncluded,  bool purchaseTaxIncluded, @JsonKey(toJson: _dateOnlyToJson)  DateTime? expiryDate, @JsonKey(toJson: _dateOnlyToJson)  DateTime? manufacturingDate)?  $default,) {final _that = this;
switch (_that) {
case _ReceivePurchaseOrderLineRequestDto() when $default != null:
return $default(_that.purchaseOrderLineId,_that.barcode,_that.batchNumber,_that.quantity,_that.totalPurchaseCost,_that.unitCost,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded,_that.expiryDate,_that.manufacturingDate);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ReceivePurchaseOrderLineRequestDto implements ReceivePurchaseOrderLineRequestDto {
  const _ReceivePurchaseOrderLineRequestDto({required this.purchaseOrderLineId, @JsonKey(toJson: _trim) required this.barcode, @JsonKey(toJson: _trim) required this.batchNumber, @JsonKey(toJson: _quantityToJson) required this.quantity, required this.totalPurchaseCost, @JsonKey(includeToJson: false) required this.unitCost, required this.mrp, required this.salesPrice, required this.taxRatePercent, required this.taxIncluded, required this.purchaseTaxIncluded, @JsonKey(toJson: _dateOnlyToJson) this.expiryDate, @JsonKey(toJson: _dateOnlyToJson) this.manufacturingDate});
  factory _ReceivePurchaseOrderLineRequestDto.fromJson(Map<String, dynamic> json) => _$ReceivePurchaseOrderLineRequestDtoFromJson(json);

@override final  String purchaseOrderLineId;
@override@JsonKey(toJson: _trim) final  String barcode;
@override@JsonKey(toJson: _trim) final  String batchNumber;
@override@JsonKey(toJson: _quantityToJson) final  double quantity;
@override final  double totalPurchaseCost;
@override@JsonKey(includeToJson: false) final  double unitCost;
@override final  double mrp;
@override final  double salesPrice;
@override final  double taxRatePercent;
@override final  bool taxIncluded;
@override final  bool purchaseTaxIncluded;
@override@JsonKey(toJson: _dateOnlyToJson) final  DateTime? expiryDate;
@override@JsonKey(toJson: _dateOnlyToJson) final  DateTime? manufacturingDate;

/// Create a copy of ReceivePurchaseOrderLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ReceivePurchaseOrderLineRequestDtoCopyWith<_ReceivePurchaseOrderLineRequestDto> get copyWith => __$ReceivePurchaseOrderLineRequestDtoCopyWithImpl<_ReceivePurchaseOrderLineRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ReceivePurchaseOrderLineRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ReceivePurchaseOrderLineRequestDto&&(identical(other.purchaseOrderLineId, purchaseOrderLineId) || other.purchaseOrderLineId == purchaseOrderLineId)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.totalPurchaseCost, totalPurchaseCost) || other.totalPurchaseCost == totalPurchaseCost)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.purchaseTaxIncluded, purchaseTaxIncluded) || other.purchaseTaxIncluded == purchaseTaxIncluded)&&(identical(other.expiryDate, expiryDate) || other.expiryDate == expiryDate)&&(identical(other.manufacturingDate, manufacturingDate) || other.manufacturingDate == manufacturingDate));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,purchaseOrderLineId,barcode,batchNumber,quantity,totalPurchaseCost,unitCost,mrp,salesPrice,taxRatePercent,taxIncluded,purchaseTaxIncluded,expiryDate,manufacturingDate);

@override
String toString() {
  return 'ReceivePurchaseOrderLineRequestDto(purchaseOrderLineId: $purchaseOrderLineId, barcode: $barcode, batchNumber: $batchNumber, quantity: $quantity, totalPurchaseCost: $totalPurchaseCost, unitCost: $unitCost, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, purchaseTaxIncluded: $purchaseTaxIncluded, expiryDate: $expiryDate, manufacturingDate: $manufacturingDate)';
}


}

/// @nodoc
abstract mixin class _$ReceivePurchaseOrderLineRequestDtoCopyWith<$Res> implements $ReceivePurchaseOrderLineRequestDtoCopyWith<$Res> {
  factory _$ReceivePurchaseOrderLineRequestDtoCopyWith(_ReceivePurchaseOrderLineRequestDto value, $Res Function(_ReceivePurchaseOrderLineRequestDto) _then) = __$ReceivePurchaseOrderLineRequestDtoCopyWithImpl;
@override @useResult
$Res call({
 String purchaseOrderLineId,@JsonKey(toJson: _trim) String barcode,@JsonKey(toJson: _trim) String batchNumber,@JsonKey(toJson: _quantityToJson) double quantity, double totalPurchaseCost,@JsonKey(includeToJson: false) double unitCost, double mrp, double salesPrice, double taxRatePercent, bool taxIncluded, bool purchaseTaxIncluded,@JsonKey(toJson: _dateOnlyToJson) DateTime? expiryDate,@JsonKey(toJson: _dateOnlyToJson) DateTime? manufacturingDate
});




}
/// @nodoc
class __$ReceivePurchaseOrderLineRequestDtoCopyWithImpl<$Res>
    implements _$ReceivePurchaseOrderLineRequestDtoCopyWith<$Res> {
  __$ReceivePurchaseOrderLineRequestDtoCopyWithImpl(this._self, this._then);

  final _ReceivePurchaseOrderLineRequestDto _self;
  final $Res Function(_ReceivePurchaseOrderLineRequestDto) _then;

/// Create a copy of ReceivePurchaseOrderLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? purchaseOrderLineId = null,Object? barcode = null,Object? batchNumber = null,Object? quantity = null,Object? totalPurchaseCost = null,Object? unitCost = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? purchaseTaxIncluded = null,Object? expiryDate = freezed,Object? manufacturingDate = freezed,}) {
  return _then(_ReceivePurchaseOrderLineRequestDto(
purchaseOrderLineId: null == purchaseOrderLineId ? _self.purchaseOrderLineId : purchaseOrderLineId // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,totalPurchaseCost: null == totalPurchaseCost ? _self.totalPurchaseCost : totalPurchaseCost // ignore: cast_nullable_to_non_nullable
as double,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,purchaseTaxIncluded: null == purchaseTaxIncluded ? _self.purchaseTaxIncluded : purchaseTaxIncluded // ignore: cast_nullable_to_non_nullable
as bool,expiryDate: freezed == expiryDate ? _self.expiryDate : expiryDate // ignore: cast_nullable_to_non_nullable
as DateTime?,manufacturingDate: freezed == manufacturingDate ? _self.manufacturingDate : manufacturingDate // ignore: cast_nullable_to_non_nullable
as DateTime?,
  ));
}


}


/// @nodoc
mixin _$ReceivePurchaseOrderRequestDto {

@JsonKey(toJson: _trimNullable) String? get referenceNumber;@JsonKey(toJson: _trimNullable) String? get notes; DateTime get receivedAt;@JsonKey(toJson: _receiveLineRequestDtosToJson) List<ReceivePurchaseOrderLineRequestDto> get lines;
/// Create a copy of ReceivePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ReceivePurchaseOrderRequestDtoCopyWith<ReceivePurchaseOrderRequestDto> get copyWith => _$ReceivePurchaseOrderRequestDtoCopyWithImpl<ReceivePurchaseOrderRequestDto>(this as ReceivePurchaseOrderRequestDto, _$identity);

  /// Serializes this ReceivePurchaseOrderRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ReceivePurchaseOrderRequestDto&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.receivedAt, receivedAt) || other.receivedAt == receivedAt)&&const DeepCollectionEquality().equals(other.lines, lines));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,referenceNumber,notes,receivedAt,const DeepCollectionEquality().hash(lines));

@override
String toString() {
  return 'ReceivePurchaseOrderRequestDto(referenceNumber: $referenceNumber, notes: $notes, receivedAt: $receivedAt, lines: $lines)';
}


}

/// @nodoc
abstract mixin class $ReceivePurchaseOrderRequestDtoCopyWith<$Res>  {
  factory $ReceivePurchaseOrderRequestDtoCopyWith(ReceivePurchaseOrderRequestDto value, $Res Function(ReceivePurchaseOrderRequestDto) _then) = _$ReceivePurchaseOrderRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(toJson: _trimNullable) String? referenceNumber,@JsonKey(toJson: _trimNullable) String? notes, DateTime receivedAt,@JsonKey(toJson: _receiveLineRequestDtosToJson) List<ReceivePurchaseOrderLineRequestDto> lines
});




}
/// @nodoc
class _$ReceivePurchaseOrderRequestDtoCopyWithImpl<$Res>
    implements $ReceivePurchaseOrderRequestDtoCopyWith<$Res> {
  _$ReceivePurchaseOrderRequestDtoCopyWithImpl(this._self, this._then);

  final ReceivePurchaseOrderRequestDto _self;
  final $Res Function(ReceivePurchaseOrderRequestDto) _then;

/// Create a copy of ReceivePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? referenceNumber = freezed,Object? notes = freezed,Object? receivedAt = null,Object? lines = null,}) {
  return _then(_self.copyWith(
referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,receivedAt: null == receivedAt ? _self.receivedAt : receivedAt // ignore: cast_nullable_to_non_nullable
as DateTime,lines: null == lines ? _self.lines : lines // ignore: cast_nullable_to_non_nullable
as List<ReceivePurchaseOrderLineRequestDto>,
  ));
}

}


/// Adds pattern-matching-related methods to [ReceivePurchaseOrderRequestDto].
extension ReceivePurchaseOrderRequestDtoPatterns on ReceivePurchaseOrderRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ReceivePurchaseOrderRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ReceivePurchaseOrderRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ReceivePurchaseOrderRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _ReceivePurchaseOrderRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ReceivePurchaseOrderRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _ReceivePurchaseOrderRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(toJson: _trimNullable)  String? referenceNumber, @JsonKey(toJson: _trimNullable)  String? notes,  DateTime receivedAt, @JsonKey(toJson: _receiveLineRequestDtosToJson)  List<ReceivePurchaseOrderLineRequestDto> lines)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ReceivePurchaseOrderRequestDto() when $default != null:
return $default(_that.referenceNumber,_that.notes,_that.receivedAt,_that.lines);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(toJson: _trimNullable)  String? referenceNumber, @JsonKey(toJson: _trimNullable)  String? notes,  DateTime receivedAt, @JsonKey(toJson: _receiveLineRequestDtosToJson)  List<ReceivePurchaseOrderLineRequestDto> lines)  $default,) {final _that = this;
switch (_that) {
case _ReceivePurchaseOrderRequestDto():
return $default(_that.referenceNumber,_that.notes,_that.receivedAt,_that.lines);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(toJson: _trimNullable)  String? referenceNumber, @JsonKey(toJson: _trimNullable)  String? notes,  DateTime receivedAt, @JsonKey(toJson: _receiveLineRequestDtosToJson)  List<ReceivePurchaseOrderLineRequestDto> lines)?  $default,) {final _that = this;
switch (_that) {
case _ReceivePurchaseOrderRequestDto() when $default != null:
return $default(_that.referenceNumber,_that.notes,_that.receivedAt,_that.lines);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ReceivePurchaseOrderRequestDto implements ReceivePurchaseOrderRequestDto {
  const _ReceivePurchaseOrderRequestDto({@JsonKey(toJson: _trimNullable) this.referenceNumber, @JsonKey(toJson: _trimNullable) this.notes, required this.receivedAt, @JsonKey(toJson: _receiveLineRequestDtosToJson) required final  List<ReceivePurchaseOrderLineRequestDto> lines}): _lines = lines;
  factory _ReceivePurchaseOrderRequestDto.fromJson(Map<String, dynamic> json) => _$ReceivePurchaseOrderRequestDtoFromJson(json);

@override@JsonKey(toJson: _trimNullable) final  String? referenceNumber;
@override@JsonKey(toJson: _trimNullable) final  String? notes;
@override final  DateTime receivedAt;
 final  List<ReceivePurchaseOrderLineRequestDto> _lines;
@override@JsonKey(toJson: _receiveLineRequestDtosToJson) List<ReceivePurchaseOrderLineRequestDto> get lines {
  if (_lines is EqualUnmodifiableListView) return _lines;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_lines);
}


/// Create a copy of ReceivePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ReceivePurchaseOrderRequestDtoCopyWith<_ReceivePurchaseOrderRequestDto> get copyWith => __$ReceivePurchaseOrderRequestDtoCopyWithImpl<_ReceivePurchaseOrderRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ReceivePurchaseOrderRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ReceivePurchaseOrderRequestDto&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.receivedAt, receivedAt) || other.receivedAt == receivedAt)&&const DeepCollectionEquality().equals(other._lines, _lines));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,referenceNumber,notes,receivedAt,const DeepCollectionEquality().hash(_lines));

@override
String toString() {
  return 'ReceivePurchaseOrderRequestDto(referenceNumber: $referenceNumber, notes: $notes, receivedAt: $receivedAt, lines: $lines)';
}


}

/// @nodoc
abstract mixin class _$ReceivePurchaseOrderRequestDtoCopyWith<$Res> implements $ReceivePurchaseOrderRequestDtoCopyWith<$Res> {
  factory _$ReceivePurchaseOrderRequestDtoCopyWith(_ReceivePurchaseOrderRequestDto value, $Res Function(_ReceivePurchaseOrderRequestDto) _then) = __$ReceivePurchaseOrderRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(toJson: _trimNullable) String? referenceNumber,@JsonKey(toJson: _trimNullable) String? notes, DateTime receivedAt,@JsonKey(toJson: _receiveLineRequestDtosToJson) List<ReceivePurchaseOrderLineRequestDto> lines
});




}
/// @nodoc
class __$ReceivePurchaseOrderRequestDtoCopyWithImpl<$Res>
    implements _$ReceivePurchaseOrderRequestDtoCopyWith<$Res> {
  __$ReceivePurchaseOrderRequestDtoCopyWithImpl(this._self, this._then);

  final _ReceivePurchaseOrderRequestDto _self;
  final $Res Function(_ReceivePurchaseOrderRequestDto) _then;

/// Create a copy of ReceivePurchaseOrderRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? referenceNumber = freezed,Object? notes = freezed,Object? receivedAt = null,Object? lines = null,}) {
  return _then(_ReceivePurchaseOrderRequestDto(
referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,receivedAt: null == receivedAt ? _self.receivedAt : receivedAt // ignore: cast_nullable_to_non_nullable
as DateTime,lines: null == lines ? _self._lines : lines // ignore: cast_nullable_to_non_nullable
as List<ReceivePurchaseOrderLineRequestDto>,
  ));
}


}

// dart format on
