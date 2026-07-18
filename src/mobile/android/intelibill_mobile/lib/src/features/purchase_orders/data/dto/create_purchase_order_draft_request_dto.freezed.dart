// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'create_purchase_order_draft_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CreatePurchaseOrderDraftLineRequestDto {

 String get itemId; String get description; int get expectedQuantity; double get unitCost;
/// Create a copy of CreatePurchaseOrderDraftLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreatePurchaseOrderDraftLineRequestDtoCopyWith<CreatePurchaseOrderDraftLineRequestDto> get copyWith => _$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl<CreatePurchaseOrderDraftLineRequestDto>(this as CreatePurchaseOrderDraftLineRequestDto, _$identity);

  /// Serializes this CreatePurchaseOrderDraftLineRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreatePurchaseOrderDraftLineRequestDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.description, description) || other.description == description)&&(identical(other.expectedQuantity, expectedQuantity) || other.expectedQuantity == expectedQuantity)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,description,expectedQuantity,unitCost);

@override
String toString() {
  return 'CreatePurchaseOrderDraftLineRequestDto(itemId: $itemId, description: $description, expectedQuantity: $expectedQuantity, unitCost: $unitCost)';
}


}

/// @nodoc
abstract mixin class $CreatePurchaseOrderDraftLineRequestDtoCopyWith<$Res>  {
  factory $CreatePurchaseOrderDraftLineRequestDtoCopyWith(CreatePurchaseOrderDraftLineRequestDto value, $Res Function(CreatePurchaseOrderDraftLineRequestDto) _then) = _$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl;
@useResult
$Res call({
 String itemId, String description, int expectedQuantity, double unitCost
});




}
/// @nodoc
class _$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl<$Res>
    implements $CreatePurchaseOrderDraftLineRequestDtoCopyWith<$Res> {
  _$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl(this._self, this._then);

  final CreatePurchaseOrderDraftLineRequestDto _self;
  final $Res Function(CreatePurchaseOrderDraftLineRequestDto) _then;

/// Create a copy of CreatePurchaseOrderDraftLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? itemId = null,Object? description = null,Object? expectedQuantity = null,Object? unitCost = null,}) {
  return _then(_self.copyWith(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,expectedQuantity: null == expectedQuantity ? _self.expectedQuantity : expectedQuantity // ignore: cast_nullable_to_non_nullable
as int,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [CreatePurchaseOrderDraftLineRequestDto].
extension CreatePurchaseOrderDraftLineRequestDtoPatterns on CreatePurchaseOrderDraftLineRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreatePurchaseOrderDraftLineRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftLineRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreatePurchaseOrderDraftLineRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftLineRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreatePurchaseOrderDraftLineRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftLineRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String itemId,  String description,  int expectedQuantity,  double unitCost)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftLineRequestDto() when $default != null:
return $default(_that.itemId,_that.description,_that.expectedQuantity,_that.unitCost);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String itemId,  String description,  int expectedQuantity,  double unitCost)  $default,) {final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftLineRequestDto():
return $default(_that.itemId,_that.description,_that.expectedQuantity,_that.unitCost);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String itemId,  String description,  int expectedQuantity,  double unitCost)?  $default,) {final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftLineRequestDto() when $default != null:
return $default(_that.itemId,_that.description,_that.expectedQuantity,_that.unitCost);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreatePurchaseOrderDraftLineRequestDto implements CreatePurchaseOrderDraftLineRequestDto {
  const _CreatePurchaseOrderDraftLineRequestDto({required this.itemId, required this.description, required this.expectedQuantity, required this.unitCost});
  factory _CreatePurchaseOrderDraftLineRequestDto.fromJson(Map<String, dynamic> json) => _$CreatePurchaseOrderDraftLineRequestDtoFromJson(json);

@override final  String itemId;
@override final  String description;
@override final  int expectedQuantity;
@override final  double unitCost;

/// Create a copy of CreatePurchaseOrderDraftLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreatePurchaseOrderDraftLineRequestDtoCopyWith<_CreatePurchaseOrderDraftLineRequestDto> get copyWith => __$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl<_CreatePurchaseOrderDraftLineRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreatePurchaseOrderDraftLineRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreatePurchaseOrderDraftLineRequestDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.description, description) || other.description == description)&&(identical(other.expectedQuantity, expectedQuantity) || other.expectedQuantity == expectedQuantity)&&(identical(other.unitCost, unitCost) || other.unitCost == unitCost));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,description,expectedQuantity,unitCost);

@override
String toString() {
  return 'CreatePurchaseOrderDraftLineRequestDto(itemId: $itemId, description: $description, expectedQuantity: $expectedQuantity, unitCost: $unitCost)';
}


}

/// @nodoc
abstract mixin class _$CreatePurchaseOrderDraftLineRequestDtoCopyWith<$Res> implements $CreatePurchaseOrderDraftLineRequestDtoCopyWith<$Res> {
  factory _$CreatePurchaseOrderDraftLineRequestDtoCopyWith(_CreatePurchaseOrderDraftLineRequestDto value, $Res Function(_CreatePurchaseOrderDraftLineRequestDto) _then) = __$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl;
@override @useResult
$Res call({
 String itemId, String description, int expectedQuantity, double unitCost
});




}
/// @nodoc
class __$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl<$Res>
    implements _$CreatePurchaseOrderDraftLineRequestDtoCopyWith<$Res> {
  __$CreatePurchaseOrderDraftLineRequestDtoCopyWithImpl(this._self, this._then);

  final _CreatePurchaseOrderDraftLineRequestDto _self;
  final $Res Function(_CreatePurchaseOrderDraftLineRequestDto) _then;

/// Create a copy of CreatePurchaseOrderDraftLineRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? itemId = null,Object? description = null,Object? expectedQuantity = null,Object? unitCost = null,}) {
  return _then(_CreatePurchaseOrderDraftLineRequestDto(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,expectedQuantity: null == expectedQuantity ? _self.expectedQuantity : expectedQuantity // ignore: cast_nullable_to_non_nullable
as int,unitCost: null == unitCost ? _self.unitCost : unitCost // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$CreatePurchaseOrderDraftRequestDto {

 String? get supplierId; String? get orderDate; String? get expectedDeliveryDate; String? get supplierReferenceNumber; String? get notes; String? get supplierName; String? get supplierReference; List<CreatePurchaseOrderDraftLineRequestDto> get lines;
/// Create a copy of CreatePurchaseOrderDraftRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreatePurchaseOrderDraftRequestDtoCopyWith<CreatePurchaseOrderDraftRequestDto> get copyWith => _$CreatePurchaseOrderDraftRequestDtoCopyWithImpl<CreatePurchaseOrderDraftRequestDto>(this as CreatePurchaseOrderDraftRequestDto, _$identity);

  /// Serializes this CreatePurchaseOrderDraftRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreatePurchaseOrderDraftRequestDto&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.orderDate, orderDate) || other.orderDate == orderDate)&&(identical(other.expectedDeliveryDate, expectedDeliveryDate) || other.expectedDeliveryDate == expectedDeliveryDate)&&(identical(other.supplierReferenceNumber, supplierReferenceNumber) || other.supplierReferenceNumber == supplierReferenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&const DeepCollectionEquality().equals(other.lines, lines));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,supplierId,orderDate,expectedDeliveryDate,supplierReferenceNumber,notes,supplierName,supplierReference,const DeepCollectionEquality().hash(lines));

@override
String toString() {
  return 'CreatePurchaseOrderDraftRequestDto(supplierId: $supplierId, orderDate: $orderDate, expectedDeliveryDate: $expectedDeliveryDate, supplierReferenceNumber: $supplierReferenceNumber, notes: $notes, supplierName: $supplierName, supplierReference: $supplierReference, lines: $lines)';
}


}

/// @nodoc
abstract mixin class $CreatePurchaseOrderDraftRequestDtoCopyWith<$Res>  {
  factory $CreatePurchaseOrderDraftRequestDtoCopyWith(CreatePurchaseOrderDraftRequestDto value, $Res Function(CreatePurchaseOrderDraftRequestDto) _then) = _$CreatePurchaseOrderDraftRequestDtoCopyWithImpl;
@useResult
$Res call({
 String? supplierId, String? orderDate, String? expectedDeliveryDate, String? supplierReferenceNumber, String? notes, String? supplierName, String? supplierReference, List<CreatePurchaseOrderDraftLineRequestDto> lines
});




}
/// @nodoc
class _$CreatePurchaseOrderDraftRequestDtoCopyWithImpl<$Res>
    implements $CreatePurchaseOrderDraftRequestDtoCopyWith<$Res> {
  _$CreatePurchaseOrderDraftRequestDtoCopyWithImpl(this._self, this._then);

  final CreatePurchaseOrderDraftRequestDto _self;
  final $Res Function(CreatePurchaseOrderDraftRequestDto) _then;

/// Create a copy of CreatePurchaseOrderDraftRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? supplierId = freezed,Object? orderDate = freezed,Object? expectedDeliveryDate = freezed,Object? supplierReferenceNumber = freezed,Object? notes = freezed,Object? supplierName = freezed,Object? supplierReference = freezed,Object? lines = null,}) {
  return _then(_self.copyWith(
supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,orderDate: freezed == orderDate ? _self.orderDate : orderDate // ignore: cast_nullable_to_non_nullable
as String?,expectedDeliveryDate: freezed == expectedDeliveryDate ? _self.expectedDeliveryDate : expectedDeliveryDate // ignore: cast_nullable_to_non_nullable
as String?,supplierReferenceNumber: freezed == supplierReferenceNumber ? _self.supplierReferenceNumber : supplierReferenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,supplierReference: freezed == supplierReference ? _self.supplierReference : supplierReference // ignore: cast_nullable_to_non_nullable
as String?,lines: null == lines ? _self.lines : lines // ignore: cast_nullable_to_non_nullable
as List<CreatePurchaseOrderDraftLineRequestDto>,
  ));
}

}


/// Adds pattern-matching-related methods to [CreatePurchaseOrderDraftRequestDto].
extension CreatePurchaseOrderDraftRequestDtoPatterns on CreatePurchaseOrderDraftRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreatePurchaseOrderDraftRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreatePurchaseOrderDraftRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreatePurchaseOrderDraftRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  String? supplierName,  String? supplierReference,  List<CreatePurchaseOrderDraftLineRequestDto> lines)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftRequestDto() when $default != null:
return $default(_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.supplierName,_that.supplierReference,_that.lines);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  String? supplierName,  String? supplierReference,  List<CreatePurchaseOrderDraftLineRequestDto> lines)  $default,) {final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftRequestDto():
return $default(_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.supplierName,_that.supplierReference,_that.lines);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String? supplierId,  String? orderDate,  String? expectedDeliveryDate,  String? supplierReferenceNumber,  String? notes,  String? supplierName,  String? supplierReference,  List<CreatePurchaseOrderDraftLineRequestDto> lines)?  $default,) {final _that = this;
switch (_that) {
case _CreatePurchaseOrderDraftRequestDto() when $default != null:
return $default(_that.supplierId,_that.orderDate,_that.expectedDeliveryDate,_that.supplierReferenceNumber,_that.notes,_that.supplierName,_that.supplierReference,_that.lines);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreatePurchaseOrderDraftRequestDto implements CreatePurchaseOrderDraftRequestDto {
  const _CreatePurchaseOrderDraftRequestDto({this.supplierId, this.orderDate, this.expectedDeliveryDate, this.supplierReferenceNumber, this.notes, this.supplierName, this.supplierReference, final  List<CreatePurchaseOrderDraftLineRequestDto> lines = const []}): _lines = lines;
  factory _CreatePurchaseOrderDraftRequestDto.fromJson(Map<String, dynamic> json) => _$CreatePurchaseOrderDraftRequestDtoFromJson(json);

@override final  String? supplierId;
@override final  String? orderDate;
@override final  String? expectedDeliveryDate;
@override final  String? supplierReferenceNumber;
@override final  String? notes;
@override final  String? supplierName;
@override final  String? supplierReference;
 final  List<CreatePurchaseOrderDraftLineRequestDto> _lines;
@override@JsonKey() List<CreatePurchaseOrderDraftLineRequestDto> get lines {
  if (_lines is EqualUnmodifiableListView) return _lines;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_lines);
}


/// Create a copy of CreatePurchaseOrderDraftRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreatePurchaseOrderDraftRequestDtoCopyWith<_CreatePurchaseOrderDraftRequestDto> get copyWith => __$CreatePurchaseOrderDraftRequestDtoCopyWithImpl<_CreatePurchaseOrderDraftRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreatePurchaseOrderDraftRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreatePurchaseOrderDraftRequestDto&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.orderDate, orderDate) || other.orderDate == orderDate)&&(identical(other.expectedDeliveryDate, expectedDeliveryDate) || other.expectedDeliveryDate == expectedDeliveryDate)&&(identical(other.supplierReferenceNumber, supplierReferenceNumber) || other.supplierReferenceNumber == supplierReferenceNumber)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.supplierReference, supplierReference) || other.supplierReference == supplierReference)&&const DeepCollectionEquality().equals(other._lines, _lines));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,supplierId,orderDate,expectedDeliveryDate,supplierReferenceNumber,notes,supplierName,supplierReference,const DeepCollectionEquality().hash(_lines));

@override
String toString() {
  return 'CreatePurchaseOrderDraftRequestDto(supplierId: $supplierId, orderDate: $orderDate, expectedDeliveryDate: $expectedDeliveryDate, supplierReferenceNumber: $supplierReferenceNumber, notes: $notes, supplierName: $supplierName, supplierReference: $supplierReference, lines: $lines)';
}


}

/// @nodoc
abstract mixin class _$CreatePurchaseOrderDraftRequestDtoCopyWith<$Res> implements $CreatePurchaseOrderDraftRequestDtoCopyWith<$Res> {
  factory _$CreatePurchaseOrderDraftRequestDtoCopyWith(_CreatePurchaseOrderDraftRequestDto value, $Res Function(_CreatePurchaseOrderDraftRequestDto) _then) = __$CreatePurchaseOrderDraftRequestDtoCopyWithImpl;
@override @useResult
$Res call({
 String? supplierId, String? orderDate, String? expectedDeliveryDate, String? supplierReferenceNumber, String? notes, String? supplierName, String? supplierReference, List<CreatePurchaseOrderDraftLineRequestDto> lines
});




}
/// @nodoc
class __$CreatePurchaseOrderDraftRequestDtoCopyWithImpl<$Res>
    implements _$CreatePurchaseOrderDraftRequestDtoCopyWith<$Res> {
  __$CreatePurchaseOrderDraftRequestDtoCopyWithImpl(this._self, this._then);

  final _CreatePurchaseOrderDraftRequestDto _self;
  final $Res Function(_CreatePurchaseOrderDraftRequestDto) _then;

/// Create a copy of CreatePurchaseOrderDraftRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? supplierId = freezed,Object? orderDate = freezed,Object? expectedDeliveryDate = freezed,Object? supplierReferenceNumber = freezed,Object? notes = freezed,Object? supplierName = freezed,Object? supplierReference = freezed,Object? lines = null,}) {
  return _then(_CreatePurchaseOrderDraftRequestDto(
supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,orderDate: freezed == orderDate ? _self.orderDate : orderDate // ignore: cast_nullable_to_non_nullable
as String?,expectedDeliveryDate: freezed == expectedDeliveryDate ? _self.expectedDeliveryDate : expectedDeliveryDate // ignore: cast_nullable_to_non_nullable
as String?,supplierReferenceNumber: freezed == supplierReferenceNumber ? _self.supplierReferenceNumber : supplierReferenceNumber // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,supplierReference: freezed == supplierReference ? _self.supplierReference : supplierReference // ignore: cast_nullable_to_non_nullable
as String?,lines: null == lines ? _self._lines : lines // ignore: cast_nullable_to_non_nullable
as List<CreatePurchaseOrderDraftLineRequestDto>,
  ));
}


}

// dart format on
