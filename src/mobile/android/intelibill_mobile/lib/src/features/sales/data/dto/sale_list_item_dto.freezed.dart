// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'sale_list_item_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SaleListItemDto {

@JsonKey(name: 'saleId') String get saleId;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'customerId') String? get customerId;@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int get paymentMethod;@JsonKey(name: 'soldAt') DateTime get soldAt;@JsonKey(name: 'paidAmount') double get paidAmount;@JsonKey(name: 'dueAmount') double get dueAmount;@JsonKey(name: 'totalBeforeDiscount') double get totalBeforeDiscount;@JsonKey(name: 'totalDiscountAmount') double get totalDiscountAmount;@JsonKey(name: 'totalAmount') double get totalAmount;@JsonKey(name: 'totalTaxAmount') double get totalTaxAmount;@JsonKey(name: 'customerName') String? get customerName;@JsonKey(name: 'customerPhone') String? get customerPhone;@JsonKey(name: 'itemCount') int get itemCount;@JsonKey(name: 'returnNumbers') List<String> get returnNumbers;@JsonKey(name: 'status') String get status;@JsonKey(name: 'refundAmount') double get refundAmount;@JsonKey(name: 'dueReductionAmount') double get dueReductionAmount;
/// Create a copy of SaleListItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleListItemDtoCopyWith<SaleListItemDto> get copyWith => _$SaleListItemDtoCopyWithImpl<SaleListItemDto>(this as SaleListItemDto, _$identity);

  /// Serializes this SaleListItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleListItemDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.paymentMethod, paymentMethod) || other.paymentMethod == paymentMethod)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&(identical(other.paidAmount, paidAmount) || other.paidAmount == paidAmount)&&(identical(other.dueAmount, dueAmount) || other.dueAmount == dueAmount)&&(identical(other.totalBeforeDiscount, totalBeforeDiscount) || other.totalBeforeDiscount == totalBeforeDiscount)&&(identical(other.totalDiscountAmount, totalDiscountAmount) || other.totalDiscountAmount == totalDiscountAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.customerName, customerName) || other.customerName == customerName)&&(identical(other.customerPhone, customerPhone) || other.customerPhone == customerPhone)&&(identical(other.itemCount, itemCount) || other.itemCount == itemCount)&&const DeepCollectionEquality().equals(other.returnNumbers, returnNumbers)&&(identical(other.status, status) || other.status == status)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleId,invoiceNumber,customerId,paymentMethod,soldAt,paidAmount,dueAmount,totalBeforeDiscount,totalDiscountAmount,totalAmount,totalTaxAmount,customerName,customerPhone,itemCount,const DeepCollectionEquality().hash(returnNumbers),status,refundAmount,dueReductionAmount);

@override
String toString() {
  return 'SaleListItemDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerId: $customerId, paymentMethod: $paymentMethod, soldAt: $soldAt, paidAmount: $paidAmount, dueAmount: $dueAmount, totalBeforeDiscount: $totalBeforeDiscount, totalDiscountAmount: $totalDiscountAmount, totalAmount: $totalAmount, totalTaxAmount: $totalTaxAmount, customerName: $customerName, customerPhone: $customerPhone, itemCount: $itemCount, returnNumbers: $returnNumbers, status: $status, refundAmount: $refundAmount, dueReductionAmount: $dueReductionAmount)';
}


}

/// @nodoc
abstract mixin class $SaleListItemDtoCopyWith<$Res>  {
  factory $SaleListItemDtoCopyWith(SaleListItemDto value, $Res Function(SaleListItemDto) _then) = _$SaleListItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerId') String? customerId,@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int paymentMethod,@JsonKey(name: 'soldAt') DateTime soldAt,@JsonKey(name: 'paidAmount') double paidAmount,@JsonKey(name: 'dueAmount') double dueAmount,@JsonKey(name: 'totalBeforeDiscount') double totalBeforeDiscount,@JsonKey(name: 'totalDiscountAmount') double totalDiscountAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'customerName') String? customerName,@JsonKey(name: 'customerPhone') String? customerPhone,@JsonKey(name: 'itemCount') int itemCount,@JsonKey(name: 'returnNumbers') List<String> returnNumbers,@JsonKey(name: 'status') String status,@JsonKey(name: 'refundAmount') double refundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount
});




}
/// @nodoc
class _$SaleListItemDtoCopyWithImpl<$Res>
    implements $SaleListItemDtoCopyWith<$Res> {
  _$SaleListItemDtoCopyWithImpl(this._self, this._then);

  final SaleListItemDto _self;
  final $Res Function(SaleListItemDto) _then;

/// Create a copy of SaleListItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerId = freezed,Object? paymentMethod = null,Object? soldAt = null,Object? paidAmount = null,Object? dueAmount = null,Object? totalBeforeDiscount = null,Object? totalDiscountAmount = null,Object? totalAmount = null,Object? totalTaxAmount = null,Object? customerName = freezed,Object? customerPhone = freezed,Object? itemCount = null,Object? returnNumbers = null,Object? status = null,Object? refundAmount = null,Object? dueReductionAmount = null,}) {
  return _then(_self.copyWith(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerId: freezed == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String?,paymentMethod: null == paymentMethod ? _self.paymentMethod : paymentMethod // ignore: cast_nullable_to_non_nullable
as int,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as DateTime,paidAmount: null == paidAmount ? _self.paidAmount : paidAmount // ignore: cast_nullable_to_non_nullable
as double,dueAmount: null == dueAmount ? _self.dueAmount : dueAmount // ignore: cast_nullable_to_non_nullable
as double,totalBeforeDiscount: null == totalBeforeDiscount ? _self.totalBeforeDiscount : totalBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,totalDiscountAmount: null == totalDiscountAmount ? _self.totalDiscountAmount : totalDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,customerPhone: freezed == customerPhone ? _self.customerPhone : customerPhone // ignore: cast_nullable_to_non_nullable
as String?,itemCount: null == itemCount ? _self.itemCount : itemCount // ignore: cast_nullable_to_non_nullable
as int,returnNumbers: null == returnNumbers ? _self.returnNumbers : returnNumbers // ignore: cast_nullable_to_non_nullable
as List<String>,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleListItemDto].
extension SaleListItemDtoPatterns on SaleListItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleListItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleListItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleListItemDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleListItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleListItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleListItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'itemCount')  int itemCount, @JsonKey(name: 'returnNumbers')  List<String> returnNumbers, @JsonKey(name: 'status')  String status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleListItemDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.paymentMethod,_that.soldAt,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.customerName,_that.customerPhone,_that.itemCount,_that.returnNumbers,_that.status,_that.refundAmount,_that.dueReductionAmount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'itemCount')  int itemCount, @JsonKey(name: 'returnNumbers')  List<String> returnNumbers, @JsonKey(name: 'status')  String status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)  $default,) {final _that = this;
switch (_that) {
case _SaleListItemDto():
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.paymentMethod,_that.soldAt,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.customerName,_that.customerPhone,_that.itemCount,_that.returnNumbers,_that.status,_that.refundAmount,_that.dueReductionAmount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'itemCount')  int itemCount, @JsonKey(name: 'returnNumbers')  List<String> returnNumbers, @JsonKey(name: 'status')  String status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)?  $default,) {final _that = this;
switch (_that) {
case _SaleListItemDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.paymentMethod,_that.soldAt,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.customerName,_that.customerPhone,_that.itemCount,_that.returnNumbers,_that.status,_that.refundAmount,_that.dueReductionAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleListItemDto implements SaleListItemDto {
  const _SaleListItemDto({@JsonKey(name: 'saleId') required this.saleId, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'customerId') this.customerId, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) required this.paymentMethod, @JsonKey(name: 'soldAt') required this.soldAt, @JsonKey(name: 'paidAmount') required this.paidAmount, @JsonKey(name: 'dueAmount') required this.dueAmount, @JsonKey(name: 'totalBeforeDiscount') required this.totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount') required this.totalDiscountAmount, @JsonKey(name: 'totalAmount') required this.totalAmount, @JsonKey(name: 'totalTaxAmount') required this.totalTaxAmount, @JsonKey(name: 'customerName') this.customerName, @JsonKey(name: 'customerPhone') this.customerPhone, @JsonKey(name: 'itemCount') required this.itemCount, @JsonKey(name: 'returnNumbers') final  List<String> returnNumbers = const [], @JsonKey(name: 'status') required this.status, @JsonKey(name: 'refundAmount') this.refundAmount = 0.0, @JsonKey(name: 'dueReductionAmount') this.dueReductionAmount = 0.0}): _returnNumbers = returnNumbers;
  factory _SaleListItemDto.fromJson(Map<String, dynamic> json) => _$SaleListItemDtoFromJson(json);

@override@JsonKey(name: 'saleId') final  String saleId;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'customerId') final  String? customerId;
@override@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) final  int paymentMethod;
@override@JsonKey(name: 'soldAt') final  DateTime soldAt;
@override@JsonKey(name: 'paidAmount') final  double paidAmount;
@override@JsonKey(name: 'dueAmount') final  double dueAmount;
@override@JsonKey(name: 'totalBeforeDiscount') final  double totalBeforeDiscount;
@override@JsonKey(name: 'totalDiscountAmount') final  double totalDiscountAmount;
@override@JsonKey(name: 'totalAmount') final  double totalAmount;
@override@JsonKey(name: 'totalTaxAmount') final  double totalTaxAmount;
@override@JsonKey(name: 'customerName') final  String? customerName;
@override@JsonKey(name: 'customerPhone') final  String? customerPhone;
@override@JsonKey(name: 'itemCount') final  int itemCount;
 final  List<String> _returnNumbers;
@override@JsonKey(name: 'returnNumbers') List<String> get returnNumbers {
  if (_returnNumbers is EqualUnmodifiableListView) return _returnNumbers;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_returnNumbers);
}

@override@JsonKey(name: 'status') final  String status;
@override@JsonKey(name: 'refundAmount') final  double refundAmount;
@override@JsonKey(name: 'dueReductionAmount') final  double dueReductionAmount;

/// Create a copy of SaleListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleListItemDtoCopyWith<_SaleListItemDto> get copyWith => __$SaleListItemDtoCopyWithImpl<_SaleListItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleListItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleListItemDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.paymentMethod, paymentMethod) || other.paymentMethod == paymentMethod)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&(identical(other.paidAmount, paidAmount) || other.paidAmount == paidAmount)&&(identical(other.dueAmount, dueAmount) || other.dueAmount == dueAmount)&&(identical(other.totalBeforeDiscount, totalBeforeDiscount) || other.totalBeforeDiscount == totalBeforeDiscount)&&(identical(other.totalDiscountAmount, totalDiscountAmount) || other.totalDiscountAmount == totalDiscountAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.customerName, customerName) || other.customerName == customerName)&&(identical(other.customerPhone, customerPhone) || other.customerPhone == customerPhone)&&(identical(other.itemCount, itemCount) || other.itemCount == itemCount)&&const DeepCollectionEquality().equals(other._returnNumbers, _returnNumbers)&&(identical(other.status, status) || other.status == status)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleId,invoiceNumber,customerId,paymentMethod,soldAt,paidAmount,dueAmount,totalBeforeDiscount,totalDiscountAmount,totalAmount,totalTaxAmount,customerName,customerPhone,itemCount,const DeepCollectionEquality().hash(_returnNumbers),status,refundAmount,dueReductionAmount);

@override
String toString() {
  return 'SaleListItemDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerId: $customerId, paymentMethod: $paymentMethod, soldAt: $soldAt, paidAmount: $paidAmount, dueAmount: $dueAmount, totalBeforeDiscount: $totalBeforeDiscount, totalDiscountAmount: $totalDiscountAmount, totalAmount: $totalAmount, totalTaxAmount: $totalTaxAmount, customerName: $customerName, customerPhone: $customerPhone, itemCount: $itemCount, returnNumbers: $returnNumbers, status: $status, refundAmount: $refundAmount, dueReductionAmount: $dueReductionAmount)';
}


}

/// @nodoc
abstract mixin class _$SaleListItemDtoCopyWith<$Res> implements $SaleListItemDtoCopyWith<$Res> {
  factory _$SaleListItemDtoCopyWith(_SaleListItemDto value, $Res Function(_SaleListItemDto) _then) = __$SaleListItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerId') String? customerId,@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int paymentMethod,@JsonKey(name: 'soldAt') DateTime soldAt,@JsonKey(name: 'paidAmount') double paidAmount,@JsonKey(name: 'dueAmount') double dueAmount,@JsonKey(name: 'totalBeforeDiscount') double totalBeforeDiscount,@JsonKey(name: 'totalDiscountAmount') double totalDiscountAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'customerName') String? customerName,@JsonKey(name: 'customerPhone') String? customerPhone,@JsonKey(name: 'itemCount') int itemCount,@JsonKey(name: 'returnNumbers') List<String> returnNumbers,@JsonKey(name: 'status') String status,@JsonKey(name: 'refundAmount') double refundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount
});




}
/// @nodoc
class __$SaleListItemDtoCopyWithImpl<$Res>
    implements _$SaleListItemDtoCopyWith<$Res> {
  __$SaleListItemDtoCopyWithImpl(this._self, this._then);

  final _SaleListItemDto _self;
  final $Res Function(_SaleListItemDto) _then;

/// Create a copy of SaleListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerId = freezed,Object? paymentMethod = null,Object? soldAt = null,Object? paidAmount = null,Object? dueAmount = null,Object? totalBeforeDiscount = null,Object? totalDiscountAmount = null,Object? totalAmount = null,Object? totalTaxAmount = null,Object? customerName = freezed,Object? customerPhone = freezed,Object? itemCount = null,Object? returnNumbers = null,Object? status = null,Object? refundAmount = null,Object? dueReductionAmount = null,}) {
  return _then(_SaleListItemDto(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerId: freezed == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String?,paymentMethod: null == paymentMethod ? _self.paymentMethod : paymentMethod // ignore: cast_nullable_to_non_nullable
as int,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as DateTime,paidAmount: null == paidAmount ? _self.paidAmount : paidAmount // ignore: cast_nullable_to_non_nullable
as double,dueAmount: null == dueAmount ? _self.dueAmount : dueAmount // ignore: cast_nullable_to_non_nullable
as double,totalBeforeDiscount: null == totalBeforeDiscount ? _self.totalBeforeDiscount : totalBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,totalDiscountAmount: null == totalDiscountAmount ? _self.totalDiscountAmount : totalDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,customerPhone: freezed == customerPhone ? _self.customerPhone : customerPhone // ignore: cast_nullable_to_non_nullable
as String?,itemCount: null == itemCount ? _self.itemCount : itemCount // ignore: cast_nullable_to_non_nullable
as int,returnNumbers: null == returnNumbers ? _self._returnNumbers : returnNumbers // ignore: cast_nullable_to_non_nullable
as List<String>,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
