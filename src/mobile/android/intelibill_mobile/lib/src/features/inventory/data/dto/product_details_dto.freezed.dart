// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'product_details_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ProductDetailsDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'description') String get description;@JsonKey(name: 'uom') String get uom;@JsonKey(name: 'costPrice') double get costPrice;@JsonKey(name: 'mrp') double get mrp;@JsonKey(name: 'salesPrice') double get salesPrice;@JsonKey(name: 'supplierId') String? get supplierId;@JsonKey(name: 'supplierName') String? get supplierName;@JsonKey(name: 'taxIncluded') bool? get taxIncluded;@JsonKey(name: 'taxRatePercent') double? get taxRatePercent;
/// Create a copy of ProductDetailsDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ProductDetailsDtoCopyWith<ProductDetailsDto> get copyWith => _$ProductDetailsDtoCopyWithImpl<ProductDetailsDto>(this as ProductDetailsDto, _$identity);

  /// Serializes this ProductDetailsDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ProductDetailsDto&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.costPrice, costPrice) || other.costPrice == costPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,description,uom,costPrice,mrp,salesPrice,supplierId,supplierName,taxIncluded,taxRatePercent);

@override
String toString() {
  return 'ProductDetailsDto(name: $name, description: $description, uom: $uom, costPrice: $costPrice, mrp: $mrp, salesPrice: $salesPrice, supplierId: $supplierId, supplierName: $supplierName, taxIncluded: $taxIncluded, taxRatePercent: $taxRatePercent)';
}


}

/// @nodoc
abstract mixin class $ProductDetailsDtoCopyWith<$Res>  {
  factory $ProductDetailsDtoCopyWith(ProductDetailsDto value, $Res Function(ProductDetailsDto) _then) = _$ProductDetailsDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'description') String description,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'costPrice') double costPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'supplierName') String? supplierName,@JsonKey(name: 'taxIncluded') bool? taxIncluded,@JsonKey(name: 'taxRatePercent') double? taxRatePercent
});




}
/// @nodoc
class _$ProductDetailsDtoCopyWithImpl<$Res>
    implements $ProductDetailsDtoCopyWith<$Res> {
  _$ProductDetailsDtoCopyWithImpl(this._self, this._then);

  final ProductDetailsDto _self;
  final $Res Function(ProductDetailsDto) _then;

/// Create a copy of ProductDetailsDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? description = null,Object? uom = null,Object? costPrice = null,Object? mrp = null,Object? salesPrice = null,Object? supplierId = freezed,Object? supplierName = freezed,Object? taxIncluded = freezed,Object? taxRatePercent = freezed,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,costPrice: null == costPrice ? _self.costPrice : costPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,taxIncluded: freezed == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool?,taxRatePercent: freezed == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}

}


/// Adds pattern-matching-related methods to [ProductDetailsDto].
extension ProductDetailsDtoPatterns on ProductDetailsDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ProductDetailsDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ProductDetailsDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ProductDetailsDto value)  $default,){
final _that = this;
switch (_that) {
case _ProductDetailsDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ProductDetailsDto value)?  $default,){
final _that = this;
switch (_that) {
case _ProductDetailsDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'supplierName')  String? supplierName, @JsonKey(name: 'taxIncluded')  bool? taxIncluded, @JsonKey(name: 'taxRatePercent')  double? taxRatePercent)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ProductDetailsDto() when $default != null:
return $default(_that.name,_that.description,_that.uom,_that.costPrice,_that.mrp,_that.salesPrice,_that.supplierId,_that.supplierName,_that.taxIncluded,_that.taxRatePercent);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'supplierName')  String? supplierName, @JsonKey(name: 'taxIncluded')  bool? taxIncluded, @JsonKey(name: 'taxRatePercent')  double? taxRatePercent)  $default,) {final _that = this;
switch (_that) {
case _ProductDetailsDto():
return $default(_that.name,_that.description,_that.uom,_that.costPrice,_that.mrp,_that.salesPrice,_that.supplierId,_that.supplierName,_that.taxIncluded,_that.taxRatePercent);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'costPrice')  double costPrice, @JsonKey(name: 'mrp')  double mrp, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'supplierName')  String? supplierName, @JsonKey(name: 'taxIncluded')  bool? taxIncluded, @JsonKey(name: 'taxRatePercent')  double? taxRatePercent)?  $default,) {final _that = this;
switch (_that) {
case _ProductDetailsDto() when $default != null:
return $default(_that.name,_that.description,_that.uom,_that.costPrice,_that.mrp,_that.salesPrice,_that.supplierId,_that.supplierName,_that.taxIncluded,_that.taxRatePercent);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ProductDetailsDto implements ProductDetailsDto {
  const _ProductDetailsDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'description') required this.description, @JsonKey(name: 'uom') required this.uom, @JsonKey(name: 'costPrice') required this.costPrice, @JsonKey(name: 'mrp') required this.mrp, @JsonKey(name: 'salesPrice') required this.salesPrice, @JsonKey(name: 'supplierId') this.supplierId, @JsonKey(name: 'supplierName') this.supplierName, @JsonKey(name: 'taxIncluded') this.taxIncluded, @JsonKey(name: 'taxRatePercent') this.taxRatePercent});
  factory _ProductDetailsDto.fromJson(Map<String, dynamic> json) => _$ProductDetailsDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'description') final  String description;
@override@JsonKey(name: 'uom') final  String uom;
@override@JsonKey(name: 'costPrice') final  double costPrice;
@override@JsonKey(name: 'mrp') final  double mrp;
@override@JsonKey(name: 'salesPrice') final  double salesPrice;
@override@JsonKey(name: 'supplierId') final  String? supplierId;
@override@JsonKey(name: 'supplierName') final  String? supplierName;
@override@JsonKey(name: 'taxIncluded') final  bool? taxIncluded;
@override@JsonKey(name: 'taxRatePercent') final  double? taxRatePercent;

/// Create a copy of ProductDetailsDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ProductDetailsDtoCopyWith<_ProductDetailsDto> get copyWith => __$ProductDetailsDtoCopyWithImpl<_ProductDetailsDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ProductDetailsDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ProductDetailsDto&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.costPrice, costPrice) || other.costPrice == costPrice)&&(identical(other.mrp, mrp) || other.mrp == mrp)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.supplierName, supplierName) || other.supplierName == supplierName)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,description,uom,costPrice,mrp,salesPrice,supplierId,supplierName,taxIncluded,taxRatePercent);

@override
String toString() {
  return 'ProductDetailsDto(name: $name, description: $description, uom: $uom, costPrice: $costPrice, mrp: $mrp, salesPrice: $salesPrice, supplierId: $supplierId, supplierName: $supplierName, taxIncluded: $taxIncluded, taxRatePercent: $taxRatePercent)';
}


}

/// @nodoc
abstract mixin class _$ProductDetailsDtoCopyWith<$Res> implements $ProductDetailsDtoCopyWith<$Res> {
  factory _$ProductDetailsDtoCopyWith(_ProductDetailsDto value, $Res Function(_ProductDetailsDto) _then) = __$ProductDetailsDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'description') String description,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'costPrice') double costPrice,@JsonKey(name: 'mrp') double mrp,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'supplierName') String? supplierName,@JsonKey(name: 'taxIncluded') bool? taxIncluded,@JsonKey(name: 'taxRatePercent') double? taxRatePercent
});




}
/// @nodoc
class __$ProductDetailsDtoCopyWithImpl<$Res>
    implements _$ProductDetailsDtoCopyWith<$Res> {
  __$ProductDetailsDtoCopyWithImpl(this._self, this._then);

  final _ProductDetailsDto _self;
  final $Res Function(_ProductDetailsDto) _then;

/// Create a copy of ProductDetailsDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? description = null,Object? uom = null,Object? costPrice = null,Object? mrp = null,Object? salesPrice = null,Object? supplierId = freezed,Object? supplierName = freezed,Object? taxIncluded = freezed,Object? taxRatePercent = freezed,}) {
  return _then(_ProductDetailsDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,costPrice: null == costPrice ? _self.costPrice : costPrice // ignore: cast_nullable_to_non_nullable
as double,mrp: null == mrp ? _self.mrp : mrp // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,supplierName: freezed == supplierName ? _self.supplierName : supplierName // ignore: cast_nullable_to_non_nullable
as String?,taxIncluded: freezed == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool?,taxRatePercent: freezed == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}


}

// dart format on
