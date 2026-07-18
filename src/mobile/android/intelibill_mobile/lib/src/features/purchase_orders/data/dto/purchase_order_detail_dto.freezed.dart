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
mixin _$PurchaseOrderReceiptLineDto {

 String get receiptLineId; String get purchaseOrderLineId; String get itemId; String get inventoryBatchId; String? get batchNumber; bool? get batchVoided; String get stockTransactionId; double get quantity; double get totalPurchaseCost; double get unitCost; double get mrp; double get salesPrice; double get taxRatePercent; bool get taxIncluded; bool get purchaseTaxIncluded;
/// Create a copy of PurchaseOrderReceiptLineDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderReceiptLineDtoCopyWith<PurchaseOrderReceiptLineDto> get copyWith => _$PurchaseOrderReceiptLineDtoCopyWithImpl<PurchaseOrderReceiptLineDto>(this as PurchaseOrderReceiptLineDto, _$identity);

  /// Serializes this PurchaseOrderReceiptLineDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderReceiptLineDto&&(identical(other.receiptLineId, receiptLineId) || other.receiptLineId == receiptLineId)&&(identical(other.purchaseOrderLineId, purchaseOrderLineId) || other.purchaseOrderLineId == purchaseOrderLineId)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.batchVoided, batchVoided) || other.batchVoided == batchVoided)&&(identical(other.stockTransactionId, stockTransactionId) || other.stockTransactionId == stockTransactionId)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.totalPurchaseCost, totalPurchaseCost) || other.totalPurchaseCost == totalPurchaseCost)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.purchaseTaxIncluded, purchaseTaxIncluded) || other.purchaseTaxIncluded == purchaseTaxIncluded));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,receiptLineId,purchaseOrderLineId,itemId,inventoryBatchId,batchNumber,batchVoided,stockTransactionId,quantity,totalPurchaseCost,unitCost,mrp,salesPrice,taxRatePercent,taxIncluded,purchaseTaxIncluded);

@override
String toString() {
  return 'PurchaseOrderReceiptLineDto(receiptLineId: $receiptLineId, purchaseOrderLineId: $purchaseOrderLineId, itemId: $itemId, inventoryBatchId: $inventoryBatchId, batchNumber: $batchNumber, batchVoided: $batchVoided, stockTransactionId: $stockTransactionId, quantity: $quantity, totalPurchaseCost: $totalPurchaseCost, unitCost: $unitCost, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, purchaseTaxIncluded: $purchaseTaxIncluded)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderReceiptLineDtoCopyWith<$Res>  {
  factory $PurchaseOrderReceiptLineDtoCopyWith(PurchaseOrderReceiptLineDto value, $Res Function(PurchaseOrderReceiptLineDto) _then) = _$PurchaseOrderReceiptLineDtoCopyWithImpl;
@useResult
$Res call({
 String receiptLineId, String purchaseOrderLineId, String itemId, String inventoryBatchId, String? batchNumber, bool? batchVoided, String stockTransactionId, double quantity, double totalPurchaseCost, double unitCost, double mrp, double salesPrice, double taxRatePercent, bool taxIncluded, bool purchaseTaxIncluded
});




}
/// @nodoc
class _$PurchaseOrderReceiptLineDtoCopyWithImpl<$Res>
    implements $PurchaseOrderReceiptLineDtoCopyWith<$Res> {
  _$PurchaseOrderReceiptLineDtoCopyWithImpl(this._self, this._then);

  final PurchaseOrderReceiptLineDto _self;
  final $Res Function(PurchaseOrderReceiptLineDto) _then;

/// Create a copy of PurchaseOrderReceiptLineDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? receiptLineId = null,Object? purchaseOrderLineId = null,Object? itemId = null,Object? inventoryBatchId = null,Object? batchNumber = freezed,Object? batchVoided = freezed,Object? stockTransactionId = null,Object? quantity = null,Object? totalPurchaseCost = null,Object? unitCost = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? purchaseTaxIncluded = null,}) {
  return _then(_self.copyWith(
receiptLineId: null == receiptLineId ? _self.receiptLineId : receiptLineId // ignore: cast_nullable_to_non_nullable
as String,purchaseOrderLineId: null == purchaseOrderLineId ? _self.purchaseOrderLineId : purchaseOrderLineId // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,inventoryBatchId: null == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String,batchNumber: freezed == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String?,batchVoided: freezed == batchVoided ? _self.batchVoided : batchVoided // ignore: cast_nullable_to_non_nullable
as bool?,stockTransactionId: null == stockTransactionId ? _self.stockTransactionId : stockTransactionId // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,totalPurchaseCost: null == totalPurchaseCost ? _self.totalPurchaseCost : totalPurchaseCost // ignore: cast_nullable_to_non_nullable
as double,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,purchaseTaxIncluded: null == purchaseTaxIncluded ? _self.purchaseTaxIncluded : purchaseTaxIncluded // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [PurchaseOrderReceiptLineDto].
extension PurchaseOrderReceiptLineDtoPatterns on PurchaseOrderReceiptLineDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _PurchaseOrderReceiptLineDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _PurchaseOrderReceiptLineDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _PurchaseOrderReceiptLineDto value)  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderReceiptLineDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _PurchaseOrderReceiptLineDto value)?  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderReceiptLineDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String receiptLineId,  String purchaseOrderLineId,  String itemId,  String inventoryBatchId,  String? batchNumber,  bool? batchVoided,  String stockTransactionId,  double quantity,  double totalPurchaseCost,  double unitCost,  double mrp,  double salesPrice,  double taxRatePercent,  bool taxIncluded,  bool purchaseTaxIncluded)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderReceiptLineDto() when $default != null:
return $default(_that.receiptLineId,_that.purchaseOrderLineId,_that.itemId,_that.inventoryBatchId,_that.batchNumber,_that.batchVoided,_that.stockTransactionId,_that.quantity,_that.totalPurchaseCost,_that.unitCost,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String receiptLineId,  String purchaseOrderLineId,  String itemId,  String inventoryBatchId,  String? batchNumber,  bool? batchVoided,  String stockTransactionId,  double quantity,  double totalPurchaseCost,  double unitCost,  double mrp,  double salesPrice,  double taxRatePercent,  bool taxIncluded,  bool purchaseTaxIncluded)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderReceiptLineDto():
return $default(_that.receiptLineId,_that.purchaseOrderLineId,_that.itemId,_that.inventoryBatchId,_that.batchNumber,_that.batchVoided,_that.stockTransactionId,_that.quantity,_that.totalPurchaseCost,_that.unitCost,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String receiptLineId,  String purchaseOrderLineId,  String itemId,  String inventoryBatchId,  String? batchNumber,  bool? batchVoided,  String stockTransactionId,  double quantity,  double totalPurchaseCost,  double unitCost,  double mrp,  double salesPrice,  double taxRatePercent,  bool taxIncluded,  bool purchaseTaxIncluded)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderReceiptLineDto() when $default != null:
return $default(_that.receiptLineId,_that.purchaseOrderLineId,_that.itemId,_that.inventoryBatchId,_that.batchNumber,_that.batchVoided,_that.stockTransactionId,_that.quantity,_that.totalPurchaseCost,_that.unitCost,_that.mrp,_that.salesPrice,_that.taxRatePercent,_that.taxIncluded,_that.purchaseTaxIncluded);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderReceiptLineDto implements PurchaseOrderReceiptLineDto {
  const _PurchaseOrderReceiptLineDto({required this.receiptLineId, required this.purchaseOrderLineId, required this.itemId, required this.inventoryBatchId, this.batchNumber, this.batchVoided, required this.stockTransactionId, required this.quantity, required this.totalPurchaseCost, required this.unitCost, required this.mrp, required this.salesPrice, required this.taxRatePercent, required this.taxIncluded, required this.purchaseTaxIncluded});
  factory _PurchaseOrderReceiptLineDto.fromJson(Map<String, dynamic> json) => _$PurchaseOrderReceiptLineDtoFromJson(json);

@override final  String receiptLineId;
@override final  String purchaseOrderLineId;
@override final  String itemId;
@override final  String inventoryBatchId;
@override final  String? batchNumber;
@override final  bool? batchVoided;
@override final  String stockTransactionId;
@override final  double quantity;
@override final  double totalPurchaseCost;
@override final  double unitCost;
@override final  double mrp;
@override final  double salesPrice;
@override final  double taxRatePercent;
@override final  bool taxIncluded;
@override final  bool purchaseTaxIncluded;

/// Create a copy of PurchaseOrderReceiptLineDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PurchaseOrderReceiptLineDtoCopyWith<_PurchaseOrderReceiptLineDto> get copyWith => __$PurchaseOrderReceiptLineDtoCopyWithImpl<_PurchaseOrderReceiptLineDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PurchaseOrderReceiptLineDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderReceiptLineDto&&(identical(other.receiptLineId, receiptLineId) || other.receiptLineId == receiptLineId)&&(identical(other.purchaseOrderLineId, purchaseOrderLineId) || other.purchaseOrderLineId == purchaseOrderLineId)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.batchVoided, batchVoided) || other.batchVoided == batchVoided)&&(identical(other.stockTransactionId, stockTransactionId) || other.stockTransactionId == stockTransactionId)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.totalPurchaseCost, totalPurchaseCost) || other.totalPurchaseCost == totalPurchaseCost)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.purchaseTaxIncluded, purchaseTaxIncluded) || other.purchaseTaxIncluded == purchaseTaxIncluded));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,receiptLineId,purchaseOrderLineId,itemId,inventoryBatchId,batchNumber,batchVoided,stockTransactionId,quantity,totalPurchaseCost,unitCost,mrp,salesPrice,taxRatePercent,taxIncluded,purchaseTaxIncluded);

@override
String toString() {
  return 'PurchaseOrderReceiptLineDto(receiptLineId: $receiptLineId, purchaseOrderLineId: $purchaseOrderLineId, itemId: $itemId, inventoryBatchId: $inventoryBatchId, batchNumber: $batchNumber, batchVoided: $batchVoided, stockTransactionId: $stockTransactionId, quantity: $quantity, totalPurchaseCost: $totalPurchaseCost, unitCost: $unitCost, mrp: $mrp, salesPrice: $salesPrice, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded, purchaseTaxIncluded: $purchaseTaxIncluded)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderReceiptLineDtoCopyWith<$Res> implements $PurchaseOrderReceiptLineDtoCopyWith<$Res> {
  factory _$PurchaseOrderReceiptLineDtoCopyWith(_PurchaseOrderReceiptLineDto value, $Res Function(_PurchaseOrderReceiptLineDto) _then) = __$PurchaseOrderReceiptLineDtoCopyWithImpl;
@override @useResult
$Res call({
 String receiptLineId, String purchaseOrderLineId, String itemId, String inventoryBatchId, String? batchNumber, bool? batchVoided, String stockTransactionId, double quantity, double totalPurchaseCost, double unitCost, double mrp, double salesPrice, double taxRatePercent, bool taxIncluded, bool purchaseTaxIncluded
});




}
/// @nodoc
class __$PurchaseOrderReceiptLineDtoCopyWithImpl<$Res>
    implements _$PurchaseOrderReceiptLineDtoCopyWith<$Res> {
  __$PurchaseOrderReceiptLineDtoCopyWithImpl(this._self, this._then);

  final _PurchaseOrderReceiptLineDto _self;
  final $Res Function(_PurchaseOrderReceiptLineDto) _then;

/// Create a copy of PurchaseOrderReceiptLineDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? receiptLineId = null,Object? purchaseOrderLineId = null,Object? itemId = null,Object? inventoryBatchId = null,Object? batchNumber = freezed,Object? batchVoided = freezed,Object? stockTransactionId = null,Object? quantity = null,Object? totalPurchaseCost = null,Object? unitCost = null,Object? mrp = null,Object? salesPrice = null,Object? taxRatePercent = null,Object? taxIncluded = null,Object? purchaseTaxIncluded = null,}) {
  return _then(_PurchaseOrderReceiptLineDto(
receiptLineId: null == receiptLineId ? _self.receiptLineId : receiptLineId // ignore: cast_nullable_to_non_nullable
as String,purchaseOrderLineId: null == purchaseOrderLineId ? _self.purchaseOrderLineId : purchaseOrderLineId // ignore: cast_nullable_to_non_nullable
as String,itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,inventoryBatchId: null == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String,batchNumber: freezed == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String?,batchVoided: freezed == batchVoided ? _self.batchVoided : batchVoided // ignore: cast_nullable_to_non_nullable
as bool?,stockTransactionId: null == stockTransactionId ? _self.stockTransactionId : stockTransactionId // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,totalPurchaseCost: null == totalPurchaseCost ? _self.totalPurchaseCost : totalPurchaseCost // ignore: cast_nullable_to_non_nullable
as double,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,purchaseTaxIncluded: null == purchaseTaxIncluded ? _self.purchaseTaxIncluded : purchaseTaxIncluded // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}


/// @nodoc
mixin _$PurchaseOrderReceiptDto {

 String get receiptId; String get receiptNumber; DateTime get receivedAt; String? get referenceNumber; String? get notes; String get receivedByUserId; String? get receivedByDisplayName; List<PurchaseOrderReceiptLineDto> get lines;
/// Create a copy of PurchaseOrderReceiptDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderReceiptDtoCopyWith<PurchaseOrderReceiptDto> get copyWith => _$PurchaseOrderReceiptDtoCopyWithImpl<PurchaseOrderReceiptDto>(this as PurchaseOrderReceiptDto, _$identity);

  /// Serializes this PurchaseOrderReceiptDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderReceiptDto&&(identical(other.receiptId, receiptId) || other.receiptId == receiptId)&&(identical(other.receiptNumber, receiptNumber) || other.receiptNumber == receiptNumber)&&(identical(other.receivedAt, receivedAt) || other.receivedAt == receivedAt)&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.receivedByUserId, receivedByUserId) || other.receivedByUserId == receivedByUserId)&&(identical(other.receivedByDisplayName, receivedByDisplayName) || other.receivedByDisplayName == receivedByDisplayName)&&const DeepCollectionEquality().equals(other.lines, lines));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,receiptId,receiptNumber,receivedAt,referenceNumber,notes,receivedByUserId,receivedByDisplayName,const DeepCollectionEquality().hash(lines));

@override
String toString() {
  return 'PurchaseOrderReceiptDto(receiptId: $receiptId, receiptNumber: $receiptNumber, receivedAt: $receivedAt, referenceNumber: $referenceNumber, notes: $notes, receivedByUserId: $receivedByUserId, receivedByDisplayName: $receivedByDisplayName, lines: $lines)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderReceiptDtoCopyWith<$Res>  {
  factory $PurchaseOrderReceiptDtoCopyWith(PurchaseOrderReceiptDto value, $Res Function(PurchaseOrderReceiptDto) _then) = _$PurchaseOrderReceiptDtoCopyWithImpl;
@useResult
$Res call({
 String receiptId, String receiptNumber, DateTime receivedAt, String? referenceNumber, String? notes, String receivedByUserId, String? receivedByDisplayName, List<PurchaseOrderReceiptLineDto> lines
});




}
/// @nodoc
class _$PurchaseOrderReceiptDtoCopyWithImpl<$Res>
    implements $PurchaseOrderReceiptDtoCopyWith<$Res> {
  _$PurchaseOrderReceiptDtoCopyWithImpl(this._self, this._then);

  final PurchaseOrderReceiptDto _self;
  final $Res Function(PurchaseOrderReceiptDto) _then;

/// Create a copy of PurchaseOrderReceiptDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? receiptId = null,Object? receiptNumber = null,Object? receivedAt = null,Object? referenceNumber = freezed,Object? notes = freezed,Object? receivedByUserId = null,Object? receivedByDisplayName = freezed,Object? lines = null,}) {
  return _then(_self.copyWith(
receiptId: null == receiptId ? _self.receiptId : receiptId // ignore: cast_nullable_to_non_nullable
as String,receiptNumber: null == receiptNumber ? _self.receiptNumber : receiptNumber // ignore: cast_nullable_to_non_nullable
as String,receivedAt: null == receivedAt ? _self.receivedAt : receivedAt // ignore: cast_nullable_to_non_nullable
as DateTime,referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,receivedByUserId: null == receivedByUserId ? _self.receivedByUserId : receivedByUserId // ignore: cast_nullable_to_non_nullable
as String,receivedByDisplayName: freezed == receivedByDisplayName ? _self.receivedByDisplayName : receivedByDisplayName // ignore: cast_nullable_to_non_nullable
as String?,lines: null == lines ? _self.lines : lines // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderReceiptLineDto>,
  ));
}

}


/// Adds pattern-matching-related methods to [PurchaseOrderReceiptDto].
extension PurchaseOrderReceiptDtoPatterns on PurchaseOrderReceiptDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _PurchaseOrderReceiptDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _PurchaseOrderReceiptDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _PurchaseOrderReceiptDto value)  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderReceiptDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _PurchaseOrderReceiptDto value)?  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderReceiptDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String receiptId,  String receiptNumber,  DateTime receivedAt,  String? referenceNumber,  String? notes,  String receivedByUserId,  String? receivedByDisplayName,  List<PurchaseOrderReceiptLineDto> lines)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderReceiptDto() when $default != null:
return $default(_that.receiptId,_that.receiptNumber,_that.receivedAt,_that.referenceNumber,_that.notes,_that.receivedByUserId,_that.receivedByDisplayName,_that.lines);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String receiptId,  String receiptNumber,  DateTime receivedAt,  String? referenceNumber,  String? notes,  String receivedByUserId,  String? receivedByDisplayName,  List<PurchaseOrderReceiptLineDto> lines)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderReceiptDto():
return $default(_that.receiptId,_that.receiptNumber,_that.receivedAt,_that.referenceNumber,_that.notes,_that.receivedByUserId,_that.receivedByDisplayName,_that.lines);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String receiptId,  String receiptNumber,  DateTime receivedAt,  String? referenceNumber,  String? notes,  String receivedByUserId,  String? receivedByDisplayName,  List<PurchaseOrderReceiptLineDto> lines)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderReceiptDto() when $default != null:
return $default(_that.receiptId,_that.receiptNumber,_that.receivedAt,_that.referenceNumber,_that.notes,_that.receivedByUserId,_that.receivedByDisplayName,_that.lines);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderReceiptDto implements PurchaseOrderReceiptDto {
  const _PurchaseOrderReceiptDto({required this.receiptId, required this.receiptNumber, required this.receivedAt, this.referenceNumber, this.notes, required this.receivedByUserId, this.receivedByDisplayName, required final  List<PurchaseOrderReceiptLineDto> lines}): _lines = lines;
  factory _PurchaseOrderReceiptDto.fromJson(Map<String, dynamic> json) => _$PurchaseOrderReceiptDtoFromJson(json);

@override final  String receiptId;
@override final  String receiptNumber;
@override final  DateTime receivedAt;
@override final  String? referenceNumber;
@override final  String? notes;
@override final  String receivedByUserId;
@override final  String? receivedByDisplayName;
 final  List<PurchaseOrderReceiptLineDto> _lines;
@override List<PurchaseOrderReceiptLineDto> get lines {
  if (_lines is EqualUnmodifiableListView) return _lines;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_lines);
}


/// Create a copy of PurchaseOrderReceiptDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PurchaseOrderReceiptDtoCopyWith<_PurchaseOrderReceiptDto> get copyWith => __$PurchaseOrderReceiptDtoCopyWithImpl<_PurchaseOrderReceiptDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PurchaseOrderReceiptDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderReceiptDto&&(identical(other.receiptId, receiptId) || other.receiptId == receiptId)&&(identical(other.receiptNumber, receiptNumber) || other.receiptNumber == receiptNumber)&&(identical(other.receivedAt, receivedAt) || other.receivedAt == receivedAt)&&(identical(other.referenceNumber, referenceNumber) || other.referenceNumber == referenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.receivedByUserId, receivedByUserId) || other.receivedByUserId == receivedByUserId)&&(identical(other.receivedByDisplayName, receivedByDisplayName) || other.receivedByDisplayName == receivedByDisplayName)&&const DeepCollectionEquality().equals(other._lines, _lines));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,receiptId,receiptNumber,receivedAt,referenceNumber,notes,receivedByUserId,receivedByDisplayName,const DeepCollectionEquality().hash(_lines));

@override
String toString() {
  return 'PurchaseOrderReceiptDto(receiptId: $receiptId, receiptNumber: $receiptNumber, receivedAt: $receivedAt, referenceNumber: $referenceNumber, notes: $notes, receivedByUserId: $receivedByUserId, receivedByDisplayName: $receivedByDisplayName, lines: $lines)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderReceiptDtoCopyWith<$Res> implements $PurchaseOrderReceiptDtoCopyWith<$Res> {
  factory _$PurchaseOrderReceiptDtoCopyWith(_PurchaseOrderReceiptDto value, $Res Function(_PurchaseOrderReceiptDto) _then) = __$PurchaseOrderReceiptDtoCopyWithImpl;
@override @useResult
$Res call({
 String receiptId, String receiptNumber, DateTime receivedAt, String? referenceNumber, String? notes, String receivedByUserId, String? receivedByDisplayName, List<PurchaseOrderReceiptLineDto> lines
});




}
/// @nodoc
class __$PurchaseOrderReceiptDtoCopyWithImpl<$Res>
    implements _$PurchaseOrderReceiptDtoCopyWith<$Res> {
  __$PurchaseOrderReceiptDtoCopyWithImpl(this._self, this._then);

  final _PurchaseOrderReceiptDto _self;
  final $Res Function(_PurchaseOrderReceiptDto) _then;

/// Create a copy of PurchaseOrderReceiptDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? receiptId = null,Object? receiptNumber = null,Object? receivedAt = null,Object? referenceNumber = freezed,Object? notes = freezed,Object? receivedByUserId = null,Object? receivedByDisplayName = freezed,Object? lines = null,}) {
  return _then(_PurchaseOrderReceiptDto(
receiptId: null == receiptId ? _self.receiptId : receiptId // ignore: cast_nullable_to_non_nullable
as String,receiptNumber: null == receiptNumber ? _self.receiptNumber : receiptNumber // ignore: cast_nullable_to_non_nullable
as String,receivedAt: null == receivedAt ? _self.receivedAt : receivedAt // ignore: cast_nullable_to_non_nullable
as DateTime,referenceNumber: freezed == referenceNumber ? _self.referenceNumber : referenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,receivedByUserId: null == receivedByUserId ? _self.receivedByUserId : receivedByUserId // ignore: cast_nullable_to_non_nullable
as String,receivedByDisplayName: freezed == receivedByDisplayName ? _self.receivedByDisplayName : receivedByDisplayName // ignore: cast_nullable_to_non_nullable
as String?,lines: null == lines ? _self._lines : lines // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderReceiptLineDto>,
  ));
}


}


/// @nodoc
mixin _$PurchaseOrderDetailDto {

 String get purchaseOrderId; String get purchaseOrderNumber; String get status; String? get supplierId; String? get orderDate; String? get expectedDeliveryDate; String? get supplierReferenceNumber; String? get notes; List<PurchaseOrderLineDto> get lines; double get expectedTotal; DateTime get createdAt; String? get supplierName; String? get supplierReference; int get receivedQuantity; String? get cancellationReason; String? get closedAt; String? get closedBy; String? get closeReason; List<PurchaseOrderReceiptDto>? get receipts;
/// Create a copy of PurchaseOrderDetailDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderDetailDtoCopyWith<PurchaseOrderDetailDto> get copyWith => _$PurchaseOrderDetailDtoCopyWithImpl<PurchaseOrderDetailDto>(this as PurchaseOrderDetailDto, _$identity);

  /// Serializes this PurchaseOrderDetailDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderDetailDto&&(identical(other.purchaseOrderId, purchaseOrderId) || other.purchaseOrderId == purchaseOrderId)&&(identical(other.purchaseOrderNumber, purchaseOrderNumber) || other.purchaseOrderNumber == purchaseOrderNumber)&&(identical(other.status, status) || other.status == status)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.orderDate, orderDate) || other.orderDate == orderDate)&&(identical(other.expectedDeliveryDate, expectedDeliveryDate) || other.expectedDeliveryDate == expectedDeliveryDate)&&(identical(other.supplierReferenceNumber, supplierReferenceNumber) || other.supplierReferenceNumber == supplierReferenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&const DeepCollectionEquality().equals(other.lines, lines)&&(identical(other.expectedTotal, expectedTotal) || other.expectedTotal == expectedTotal)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity)&&(identical(other.cancellationReason, cancellationReason) || other.cancellationReason == cancellationReason)&&(identical(other.closedAt, closedAt) || other.closedAt == closedAt)&&(identical(other.closedBy, closedBy) || other.closedBy == closedBy)&&(identical(other.closeReason, closeReason) || other.closeReason == closeReason)&&const DeepCollectionEquality().equals(other.receipts, receipts));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,purchaseOrderId,purchaseOrderNumber,status,supplierId,orderDate,expectedDeliveryDate,supplierReferenceNumber,notes,const DeepCollectionEquality().hash(lines),expectedTotal,createdAt,supplierName,supplierReference,receivedQuantity,cancellationReason,closedAt,closedBy,closeReason,const DeepCollectionEquality().hash(receipts)]);

@override
String toString() {
  return 'PurchaseOrderDetailDto(purchaseOrderId: $purchaseOrderId, purchaseOrderNumber: $purchaseOrderNumber, status: $status, supplierId: $supplierId, orderDate: $orderDate, expectedDeliveryDate: $expectedDeliveryDate, supplierReferenceNumber: $supplierReferenceNumber, notes: $notes, lines: $lines, expectedTotal: $expectedTotal, createdAt: $createdAt, supplierName: $supplierName, supplierReference: $supplierReference, receivedQuantity: $receivedQuantity, cancellationReason: $cancellationReason, closedAt: $closedAt, closedBy: $closedBy, closeReason: $closeReason, receipts: $receipts)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderDetailDtoCopyWith<$Res>  {
  factory $PurchaseOrderDetailDtoCopyWith(PurchaseOrderDetailDto value, $Res Function(PurchaseOrderDetailDto) _then) = _$PurchaseOrderDetailDtoCopyWithImpl;
@useResult
$Res call({
 String purchaseOrderId, String purchaseOrderNumber, String status, String? supplierId, String? orderDate, String? expectedDeliveryDate, String? supplierReferenceNumber, String? notes, List<PurchaseOrderLineDto> lines, double expectedTotal, DateTime createdAt, String? supplierName, String? supplierReference, int receivedQuantity, String? cancellationReason, String? closedAt, String? closedBy, String? closeReason, List<PurchaseOrderReceiptDto>? receipts
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
@pragma('vm:prefer-inline') @override $Res call({Object? purchaseOrderId = null,Object? purchaseOrderNumber = null,Object? status = null,Object? supplierId = freezed,Object? orderDate = freezed,Object? expectedDeliveryDate = freezed,Object? supplierReferenceNumber = freezed,Object? notes = freezed,Object? lines = null,Object? expectedTotal = null,Object? createdAt = null,Object? supplierName = freezed,Object? supplierReference = freezed,Object? receivedQuantity = null,Object? cancellationReason = freezed,Object? closedAt = freezed,Object? closedBy = freezed,Object? closeReason = freezed,Object? receipts = freezed,}) {
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
as int,cancellationReason: freezed == cancellationReason ? _self.cancellationReason : cancellationReason // ignore: cast_nullable_to_non_nullable
as String?,closedAt: freezed == closedAt ? _self.closedAt : closedAt // ignore: cast_nullable_to_non_nullable
as String?,closedBy: freezed == closedBy ? _self.closedBy : closedBy // ignore: cast_nullable_to_non_nullable
as String?,closeReason: freezed == closeReason ? _self.closeReason : closeReason // ignore: cast_nullable_to_non_nullable
as String?,receipts: freezed == receipts ? _self.receipts : receipts // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderReceiptDto>?,
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  List<PurchaseOrderLineDto> lines,  double expectedTotal,  DateTime createdAt,  String? supplierName,  String? supplierReference,  int receivedQuantity,  String? cancellationReason,  String? closedAt,  String? closedBy,  String? closeReason,  List<PurchaseOrderReceiptDto>? receipts)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto() when $default != null:
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.lines,_that.expectedTotal,_that.createdAt,_that.supplierName,_that.supplierReference,_that.receivedQuantity,_that.cancellationReason,_that.closedAt,_that.closedBy,_that.closeReason,_that.receipts);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  List<PurchaseOrderLineDto> lines,  double expectedTotal,  DateTime createdAt,  String? supplierName,  String? supplierReference,  int receivedQuantity,  String? cancellationReason,  String? closedAt,  String? closedBy,  String? closeReason,  List<PurchaseOrderReceiptDto>? receipts)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto():
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.lines,_that.expectedTotal,_that.createdAt,_that.supplierName,_that.supplierReference,_that.receivedQuantity,_that.cancellationReason,_that.closedAt,_that.closedBy,_that.closeReason,_that.receipts);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String purchaseOrderId,  String purchaseOrderNumber,  String status,  String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  List<PurchaseOrderLineDto> lines,  double expectedTotal,  DateTime createdAt,  String? supplierName,  String? supplierReference,  int receivedQuantity,  String? cancellationReason,  String? closedAt,  String? closedBy,  String? closeReason,  List<PurchaseOrderReceiptDto>? receipts)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderDetailDto() when $default != null:
return $default(_that.purchaseOrderId,_that.purchaseOrderNumber,_that.status,_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.lines,_that.expectedTotal,_that.createdAt,_that.supplierName,_that.supplierReference,_that.receivedQuantity,_that.cancellationReason,_that.closedAt,_that.closedBy,_that.closeReason,_that.receipts);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderDetailDto implements PurchaseOrderDetailDto {
  const _PurchaseOrderDetailDto({required this.purchaseOrderId, required this.purchaseOrderNumber, required this.status, this.supplierId, this.orderDate, this.expectedDeliveryDate, this.supplierReferenceNumber, this.notes, required final  List<PurchaseOrderLineDto> lines, required this.expectedTotal, required this.createdAt, this.supplierName, this.supplierReference, required this.receivedQuantity, this.cancellationReason, this.closedAt, this.closedBy, this.closeReason, final  List<PurchaseOrderReceiptDto>? receipts}): _lines = lines,_receipts = receipts;
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
@override final  String? cancellationReason;
@override final  String? closedAt;
@override final  String? closedBy;
@override final  String? closeReason;
 final  List<PurchaseOrderReceiptDto>? _receipts;
@override List<PurchaseOrderReceiptDto>? get receipts {
  final value = _receipts;
  if (value == null) return null;
  if (_receipts is EqualUnmodifiableListView) return _receipts;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(value);
}


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
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderDetailDto&&(identical(other.purchaseOrderId, purchaseOrderId) || other.purchaseOrderId == purchaseOrderId)&&(identical(other.purchaseOrderNumber, purchaseOrderNumber) || other.purchaseOrderNumber == purchaseOrderNumber)&&(identical(other.status, status) || other.status == status)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.orderDate, orderDate) || other.orderDate == orderDate)&&(identical(other.expectedDeliveryDate, expectedDeliveryDate) || other.expectedDeliveryDate == expectedDeliveryDate)&&(identical(other.supplierReferenceNumber, supplierReferenceNumber) || other.supplierReferenceNumber == supplierReferenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&const DeepCollectionEquality().equals(other._lines, _lines)&&(identical(other.expectedTotal, expectedTotal) || other.expectedTotal == expectedTotal)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&(identical(other.receivedQuantity, receivedQuantity) || other.receivedQuantity == receivedQuantity)&&(identical(other.cancellationReason, cancellationReason) || other.cancellationReason == cancellationReason)&&(identical(other.closedAt, closedAt) || other.closedAt == closedAt)&&(identical(other.closedBy, closedBy) || other.closedBy == closedBy)&&(identical(other.closeReason, closeReason) || other.closeReason == closeReason)&&const DeepCollectionEquality().equals(other._receipts, _receipts));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,purchaseOrderId,purchaseOrderNumber,status,supplierId,orderDate,expectedDeliveryDate,supplierReferenceNumber,notes,const DeepCollectionEquality().hash(_lines),expectedTotal,createdAt,supplierName,supplierReference,receivedQuantity,cancellationReason,closedAt,closedBy,closeReason,const DeepCollectionEquality().hash(_receipts)]);

@override
String toString() {
  return 'PurchaseOrderDetailDto(purchaseOrderId: $purchaseOrderId, purchaseOrderNumber: $purchaseOrderNumber, status: $status, supplierId: $supplierId, orderDate: $orderDate, expectedDeliveryDate: $expectedDeliveryDate, supplierReferenceNumber: $supplierReferenceNumber, notes: $notes, lines: $lines, expectedTotal: $expectedTotal, createdAt: $createdAt, supplierName: $supplierName, supplierReference: $supplierReference, receivedQuantity: $receivedQuantity, cancellationReason: $cancellationReason, closedAt: $closedAt, closedBy: $closedBy, closeReason: $closeReason, receipts: $receipts)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderDetailDtoCopyWith<$Res> implements $PurchaseOrderDetailDtoCopyWith<$Res> {
  factory _$PurchaseOrderDetailDtoCopyWith(_PurchaseOrderDetailDto value, $Res Function(_PurchaseOrderDetailDto) _then) = __$PurchaseOrderDetailDtoCopyWithImpl;
@override @useResult
$Res call({
 String purchaseOrderId, String purchaseOrderNumber, String status, String? supplierId, String? orderDate, String? expectedDeliveryDate, String? supplierReferenceNumber, String? notes, List<PurchaseOrderLineDto> lines, double expectedTotal, DateTime createdAt, String? supplierName, String? supplierReference, int receivedQuantity, String? cancellationReason, String? closedAt, String? closedBy, String? closeReason, List<PurchaseOrderReceiptDto>? receipts
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
@override @pragma('vm:prefer-inline') $Res call({Object? purchaseOrderId = null,Object? purchaseOrderNumber = null,Object? status = null,Object? supplierId = freezed,Object? orderDate = freezed,Object? expectedDeliveryDate = freezed,Object? supplierReferenceNumber = freezed,Object? notes = freezed,Object? lines = null,Object? expectedTotal = null,Object? createdAt = null,Object? supplierName = freezed,Object? supplierReference = freezed,Object? receivedQuantity = null,Object? cancellationReason = freezed,Object? closedAt = freezed,Object? closedBy = freezed,Object? closeReason = freezed,Object? receipts = freezed,}) {
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
as int,cancellationReason: freezed == cancellationReason ? _self.cancellationReason : cancellationReason // ignore: cast_nullable_to_non_nullable
as String?,closedAt: freezed == closedAt ? _self.closedAt : closedAt // ignore: cast_nullable_to_non_nullable
as String?,closedBy: freezed == closedBy ? _self.closedBy : closedBy // ignore: cast_nullable_to_non_nullable
as String?,closeReason: freezed == closeReason ? _self.closeReason : closeReason // ignore: cast_nullable_to_non_nullable
as String?,receipts: freezed == receipts ? _self._receipts : receipts // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderReceiptDto>?,
  ));
}


}

// dart format on
