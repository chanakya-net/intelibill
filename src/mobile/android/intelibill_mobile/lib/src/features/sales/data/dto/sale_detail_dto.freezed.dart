// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'sale_detail_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SaleDetailDto {

@JsonKey(name: 'saleId') String get saleId;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'customerId') String? get customerId;@JsonKey(name: 'customerName') String? get customerName;@JsonKey(name: 'customerPhone') String? get customerPhone;@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int get paymentMethod;@JsonKey(name: 'soldAt') DateTime get soldAt;@JsonKey(name: 'items') List<SaleDetailItemDto> get items;@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> get settlements;@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> get discounts;@JsonKey(name: 'returns') List<SaleDetailReturnDto> get returns;@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailRedemptionDto> get redemptions;@JsonKey(name: 'warnings') List<String> get warnings;@JsonKey(name: 'paidAmount') double get paidAmount;@JsonKey(name: 'dueAmount') double get dueAmount;@JsonKey(name: 'totalBeforeDiscount') double get totalBeforeDiscount;@JsonKey(name: 'totalDiscountAmount') double get totalDiscountAmount;@JsonKey(name: 'totalAmount') double get totalAmount;@JsonKey(name: 'totalTaxAmount') double get totalTaxAmount;@JsonKey(name: 'creditNoteAppliedAmount') double get creditNoteAppliedAmount;@JsonKey(name: 'status') String? get status;@JsonKey(name: 'refundAmount') double get refundAmount;@JsonKey(name: 'dueReductionAmount') double get dueReductionAmount;
/// Create a copy of SaleDetailDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailDtoCopyWith<SaleDetailDto> get copyWith => _$SaleDetailDtoCopyWithImpl<SaleDetailDto>(this as SaleDetailDto, _$identity);

  /// Serializes this SaleDetailDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.customerName, customerName) || other.customerName == customerName)&&(identical(other.customerPhone, customerPhone) || other.customerPhone == customerPhone)&&(identical(other.paymentMethod, paymentMethod) || other.paymentMethod == paymentMethod)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&const DeepCollectionEquality().equals(other.items, items)&&const DeepCollectionEquality().equals(other.settlements, settlements)&&const DeepCollectionEquality().equals(other.discounts, discounts)&&const DeepCollectionEquality().equals(other.returns, returns)&&const DeepCollectionEquality().equals(other.redemptions, redemptions)&&const DeepCollectionEquality().equals(other.warnings, warnings)&&(identical(other.paidAmount, paidAmount) || other.paidAmount == paidAmount)&&(identical(other.dueAmount, dueAmount) || other.dueAmount == dueAmount)&&(identical(other.totalBeforeDiscount, totalBeforeDiscount) || other.totalBeforeDiscount == totalBeforeDiscount)&&(identical(other.totalDiscountAmount, totalDiscountAmount) || other.totalDiscountAmount == totalDiscountAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.creditNoteAppliedAmount, creditNoteAppliedAmount) || other.creditNoteAppliedAmount == creditNoteAppliedAmount)&&(identical(other.status, status) || other.status == status)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,saleId,invoiceNumber,customerId,customerName,customerPhone,paymentMethod,soldAt,const DeepCollectionEquality().hash(items),const DeepCollectionEquality().hash(settlements),const DeepCollectionEquality().hash(discounts),const DeepCollectionEquality().hash(returns),const DeepCollectionEquality().hash(redemptions),const DeepCollectionEquality().hash(warnings),paidAmount,dueAmount,totalBeforeDiscount,totalDiscountAmount,totalAmount,totalTaxAmount,creditNoteAppliedAmount,status,refundAmount,dueReductionAmount]);

@override
String toString() {
  return 'SaleDetailDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerId: $customerId, customerName: $customerName, customerPhone: $customerPhone, paymentMethod: $paymentMethod, soldAt: $soldAt, items: $items, settlements: $settlements, discounts: $discounts, returns: $returns, redemptions: $redemptions, warnings: $warnings, paidAmount: $paidAmount, dueAmount: $dueAmount, totalBeforeDiscount: $totalBeforeDiscount, totalDiscountAmount: $totalDiscountAmount, totalAmount: $totalAmount, totalTaxAmount: $totalTaxAmount, creditNoteAppliedAmount: $creditNoteAppliedAmount, status: $status, refundAmount: $refundAmount, dueReductionAmount: $dueReductionAmount)';
}


}

/// @nodoc
abstract mixin class $SaleDetailDtoCopyWith<$Res>  {
  factory $SaleDetailDtoCopyWith(SaleDetailDto value, $Res Function(SaleDetailDto) _then) = _$SaleDetailDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerId') String? customerId,@JsonKey(name: 'customerName') String? customerName,@JsonKey(name: 'customerPhone') String? customerPhone,@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int paymentMethod,@JsonKey(name: 'soldAt') DateTime soldAt,@JsonKey(name: 'items') List<SaleDetailItemDto> items,@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> settlements,@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> discounts,@JsonKey(name: 'returns') List<SaleDetailReturnDto> returns,@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailRedemptionDto> redemptions,@JsonKey(name: 'warnings') List<String> warnings,@JsonKey(name: 'paidAmount') double paidAmount,@JsonKey(name: 'dueAmount') double dueAmount,@JsonKey(name: 'totalBeforeDiscount') double totalBeforeDiscount,@JsonKey(name: 'totalDiscountAmount') double totalDiscountAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'creditNoteAppliedAmount') double creditNoteAppliedAmount,@JsonKey(name: 'status') String? status,@JsonKey(name: 'refundAmount') double refundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount
});




}
/// @nodoc
class _$SaleDetailDtoCopyWithImpl<$Res>
    implements $SaleDetailDtoCopyWith<$Res> {
  _$SaleDetailDtoCopyWithImpl(this._self, this._then);

  final SaleDetailDto _self;
  final $Res Function(SaleDetailDto) _then;

/// Create a copy of SaleDetailDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerId = freezed,Object? customerName = freezed,Object? customerPhone = freezed,Object? paymentMethod = null,Object? soldAt = null,Object? items = null,Object? settlements = null,Object? discounts = null,Object? returns = null,Object? redemptions = null,Object? warnings = null,Object? paidAmount = null,Object? dueAmount = null,Object? totalBeforeDiscount = null,Object? totalDiscountAmount = null,Object? totalAmount = null,Object? totalTaxAmount = null,Object? creditNoteAppliedAmount = null,Object? status = freezed,Object? refundAmount = null,Object? dueReductionAmount = null,}) {
  return _then(_self.copyWith(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerId: freezed == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String?,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,customerPhone: freezed == customerPhone ? _self.customerPhone : customerPhone // ignore: cast_nullable_to_non_nullable
as String?,paymentMethod: null == paymentMethod ? _self.paymentMethod : paymentMethod // ignore: cast_nullable_to_non_nullable
as int,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as DateTime,items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailItemDto>,settlements: null == settlements ? _self.settlements : settlements // ignore: cast_nullable_to_non_nullable
as List<SaleDetailSettlementDto>,discounts: null == discounts ? _self.discounts : discounts // ignore: cast_nullable_to_non_nullable
as List<SaleDetailDiscountDto>,returns: null == returns ? _self.returns : returns // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnDto>,redemptions: null == redemptions ? _self.redemptions : redemptions // ignore: cast_nullable_to_non_nullable
as List<SaleDetailRedemptionDto>,warnings: null == warnings ? _self.warnings : warnings // ignore: cast_nullable_to_non_nullable
as List<String>,paidAmount: null == paidAmount ? _self.paidAmount : paidAmount // ignore: cast_nullable_to_non_nullable
as double,dueAmount: null == dueAmount ? _self.dueAmount : dueAmount // ignore: cast_nullable_to_non_nullable
as double,totalBeforeDiscount: null == totalBeforeDiscount ? _self.totalBeforeDiscount : totalBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,totalDiscountAmount: null == totalDiscountAmount ? _self.totalDiscountAmount : totalDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,creditNoteAppliedAmount: null == creditNoteAppliedAmount ? _self.creditNoteAppliedAmount : creditNoteAppliedAmount // ignore: cast_nullable_to_non_nullable
as double,status: freezed == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String?,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailDto].
extension SaleDetailDtoPatterns on SaleDetailDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'items')  List<SaleDetailItemDto> items, @JsonKey(name: 'settlements')  List<SaleDetailSettlementDto> settlements, @JsonKey(name: 'discounts')  List<SaleDetailDiscountDto> discounts, @JsonKey(name: 'returns')  List<SaleDetailReturnDto> returns, @JsonKey(name: 'creditNoteRedemptions')  List<SaleDetailRedemptionDto> redemptions, @JsonKey(name: 'warnings')  List<String> warnings, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount')  double creditNoteAppliedAmount, @JsonKey(name: 'status')  String? status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.customerName,_that.customerPhone,_that.paymentMethod,_that.soldAt,_that.items,_that.settlements,_that.discounts,_that.returns,_that.redemptions,_that.warnings,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.creditNoteAppliedAmount,_that.status,_that.refundAmount,_that.dueReductionAmount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'items')  List<SaleDetailItemDto> items, @JsonKey(name: 'settlements')  List<SaleDetailSettlementDto> settlements, @JsonKey(name: 'discounts')  List<SaleDetailDiscountDto> discounts, @JsonKey(name: 'returns')  List<SaleDetailReturnDto> returns, @JsonKey(name: 'creditNoteRedemptions')  List<SaleDetailRedemptionDto> redemptions, @JsonKey(name: 'warnings')  List<String> warnings, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount')  double creditNoteAppliedAmount, @JsonKey(name: 'status')  String? status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailDto():
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.customerName,_that.customerPhone,_that.paymentMethod,_that.soldAt,_that.items,_that.settlements,_that.discounts,_that.returns,_that.redemptions,_that.warnings,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.creditNoteAppliedAmount,_that.status,_that.refundAmount,_that.dueReductionAmount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'items')  List<SaleDetailItemDto> items, @JsonKey(name: 'settlements')  List<SaleDetailSettlementDto> settlements, @JsonKey(name: 'discounts')  List<SaleDetailDiscountDto> discounts, @JsonKey(name: 'returns')  List<SaleDetailReturnDto> returns, @JsonKey(name: 'creditNoteRedemptions')  List<SaleDetailRedemptionDto> redemptions, @JsonKey(name: 'warnings')  List<String> warnings, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount')  double creditNoteAppliedAmount, @JsonKey(name: 'status')  String? status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.customerName,_that.customerPhone,_that.paymentMethod,_that.soldAt,_that.items,_that.settlements,_that.discounts,_that.returns,_that.redemptions,_that.warnings,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.creditNoteAppliedAmount,_that.status,_that.refundAmount,_that.dueReductionAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailDto implements SaleDetailDto {
  const _SaleDetailDto({@JsonKey(name: 'saleId') required this.saleId, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'customerId') this.customerId, @JsonKey(name: 'customerName') this.customerName, @JsonKey(name: 'customerPhone') this.customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) required this.paymentMethod, @JsonKey(name: 'soldAt') required this.soldAt, @JsonKey(name: 'items') final  List<SaleDetailItemDto> items = const [], @JsonKey(name: 'settlements') final  List<SaleDetailSettlementDto> settlements = const [], @JsonKey(name: 'discounts') final  List<SaleDetailDiscountDto> discounts = const [], @JsonKey(name: 'returns') final  List<SaleDetailReturnDto> returns = const [], @JsonKey(name: 'creditNoteRedemptions') final  List<SaleDetailRedemptionDto> redemptions = const [], @JsonKey(name: 'warnings') final  List<String> warnings = const [], @JsonKey(name: 'paidAmount') required this.paidAmount, @JsonKey(name: 'dueAmount') required this.dueAmount, @JsonKey(name: 'totalBeforeDiscount') required this.totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount') required this.totalDiscountAmount, @JsonKey(name: 'totalAmount') required this.totalAmount, @JsonKey(name: 'totalTaxAmount') required this.totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount') this.creditNoteAppliedAmount = 0.0, @JsonKey(name: 'status') this.status, @JsonKey(name: 'refundAmount') this.refundAmount = 0.0, @JsonKey(name: 'dueReductionAmount') this.dueReductionAmount = 0.0}): _items = items,_settlements = settlements,_discounts = discounts,_returns = returns,_redemptions = redemptions,_warnings = warnings;
  factory _SaleDetailDto.fromJson(Map<String, dynamic> json) => _$SaleDetailDtoFromJson(json);

@override@JsonKey(name: 'saleId') final  String saleId;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'customerId') final  String? customerId;
@override@JsonKey(name: 'customerName') final  String? customerName;
@override@JsonKey(name: 'customerPhone') final  String? customerPhone;
@override@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) final  int paymentMethod;
@override@JsonKey(name: 'soldAt') final  DateTime soldAt;
 final  List<SaleDetailItemDto> _items;
@override@JsonKey(name: 'items') List<SaleDetailItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

 final  List<SaleDetailSettlementDto> _settlements;
@override@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> get settlements {
  if (_settlements is EqualUnmodifiableListView) return _settlements;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_settlements);
}

 final  List<SaleDetailDiscountDto> _discounts;
@override@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> get discounts {
  if (_discounts is EqualUnmodifiableListView) return _discounts;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_discounts);
}

 final  List<SaleDetailReturnDto> _returns;
@override@JsonKey(name: 'returns') List<SaleDetailReturnDto> get returns {
  if (_returns is EqualUnmodifiableListView) return _returns;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_returns);
}

 final  List<SaleDetailRedemptionDto> _redemptions;
@override@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailRedemptionDto> get redemptions {
  if (_redemptions is EqualUnmodifiableListView) return _redemptions;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_redemptions);
}

 final  List<String> _warnings;
@override@JsonKey(name: 'warnings') List<String> get warnings {
  if (_warnings is EqualUnmodifiableListView) return _warnings;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_warnings);
}

@override@JsonKey(name: 'paidAmount') final  double paidAmount;
@override@JsonKey(name: 'dueAmount') final  double dueAmount;
@override@JsonKey(name: 'totalBeforeDiscount') final  double totalBeforeDiscount;
@override@JsonKey(name: 'totalDiscountAmount') final  double totalDiscountAmount;
@override@JsonKey(name: 'totalAmount') final  double totalAmount;
@override@JsonKey(name: 'totalTaxAmount') final  double totalTaxAmount;
@override@JsonKey(name: 'creditNoteAppliedAmount') final  double creditNoteAppliedAmount;
@override@JsonKey(name: 'status') final  String? status;
@override@JsonKey(name: 'refundAmount') final  double refundAmount;
@override@JsonKey(name: 'dueReductionAmount') final  double dueReductionAmount;

/// Create a copy of SaleDetailDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailDtoCopyWith<_SaleDetailDto> get copyWith => __$SaleDetailDtoCopyWithImpl<_SaleDetailDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.customerName, customerName) || other.customerName == customerName)&&(identical(other.customerPhone, customerPhone) || other.customerPhone == customerPhone)&&(identical(other.paymentMethod, paymentMethod) || other.paymentMethod == paymentMethod)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&const DeepCollectionEquality().equals(other._items, _items)&&const DeepCollectionEquality().equals(other._settlements, _settlements)&&const DeepCollectionEquality().equals(other._discounts, _discounts)&&const DeepCollectionEquality().equals(other._returns, _returns)&&const DeepCollectionEquality().equals(other._redemptions, _redemptions)&&const DeepCollectionEquality().equals(other._warnings, _warnings)&&(identical(other.paidAmount, paidAmount) || other.paidAmount == paidAmount)&&(identical(other.dueAmount, dueAmount) || other.dueAmount == dueAmount)&&(identical(other.totalBeforeDiscount, totalBeforeDiscount) || other.totalBeforeDiscount == totalBeforeDiscount)&&(identical(other.totalDiscountAmount, totalDiscountAmount) || other.totalDiscountAmount == totalDiscountAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.creditNoteAppliedAmount, creditNoteAppliedAmount) || other.creditNoteAppliedAmount == creditNoteAppliedAmount)&&(identical(other.status, status) || other.status == status)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,saleId,invoiceNumber,customerId,customerName,customerPhone,paymentMethod,soldAt,const DeepCollectionEquality().hash(_items),const DeepCollectionEquality().hash(_settlements),const DeepCollectionEquality().hash(_discounts),const DeepCollectionEquality().hash(_returns),const DeepCollectionEquality().hash(_redemptions),const DeepCollectionEquality().hash(_warnings),paidAmount,dueAmount,totalBeforeDiscount,totalDiscountAmount,totalAmount,totalTaxAmount,creditNoteAppliedAmount,status,refundAmount,dueReductionAmount]);

@override
String toString() {
  return 'SaleDetailDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerId: $customerId, customerName: $customerName, customerPhone: $customerPhone, paymentMethod: $paymentMethod, soldAt: $soldAt, items: $items, settlements: $settlements, discounts: $discounts, returns: $returns, redemptions: $redemptions, warnings: $warnings, paidAmount: $paidAmount, dueAmount: $dueAmount, totalBeforeDiscount: $totalBeforeDiscount, totalDiscountAmount: $totalDiscountAmount, totalAmount: $totalAmount, totalTaxAmount: $totalTaxAmount, creditNoteAppliedAmount: $creditNoteAppliedAmount, status: $status, refundAmount: $refundAmount, dueReductionAmount: $dueReductionAmount)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailDtoCopyWith<$Res> implements $SaleDetailDtoCopyWith<$Res> {
  factory _$SaleDetailDtoCopyWith(_SaleDetailDto value, $Res Function(_SaleDetailDto) _then) = __$SaleDetailDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerId') String? customerId,@JsonKey(name: 'customerName') String? customerName,@JsonKey(name: 'customerPhone') String? customerPhone,@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int paymentMethod,@JsonKey(name: 'soldAt') DateTime soldAt,@JsonKey(name: 'items') List<SaleDetailItemDto> items,@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> settlements,@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> discounts,@JsonKey(name: 'returns') List<SaleDetailReturnDto> returns,@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailRedemptionDto> redemptions,@JsonKey(name: 'warnings') List<String> warnings,@JsonKey(name: 'paidAmount') double paidAmount,@JsonKey(name: 'dueAmount') double dueAmount,@JsonKey(name: 'totalBeforeDiscount') double totalBeforeDiscount,@JsonKey(name: 'totalDiscountAmount') double totalDiscountAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'creditNoteAppliedAmount') double creditNoteAppliedAmount,@JsonKey(name: 'status') String? status,@JsonKey(name: 'refundAmount') double refundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount
});




}
/// @nodoc
class __$SaleDetailDtoCopyWithImpl<$Res>
    implements _$SaleDetailDtoCopyWith<$Res> {
  __$SaleDetailDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailDto _self;
  final $Res Function(_SaleDetailDto) _then;

/// Create a copy of SaleDetailDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerId = freezed,Object? customerName = freezed,Object? customerPhone = freezed,Object? paymentMethod = null,Object? soldAt = null,Object? items = null,Object? settlements = null,Object? discounts = null,Object? returns = null,Object? redemptions = null,Object? warnings = null,Object? paidAmount = null,Object? dueAmount = null,Object? totalBeforeDiscount = null,Object? totalDiscountAmount = null,Object? totalAmount = null,Object? totalTaxAmount = null,Object? creditNoteAppliedAmount = null,Object? status = freezed,Object? refundAmount = null,Object? dueReductionAmount = null,}) {
  return _then(_SaleDetailDto(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerId: freezed == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String?,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,customerPhone: freezed == customerPhone ? _self.customerPhone : customerPhone // ignore: cast_nullable_to_non_nullable
as String?,paymentMethod: null == paymentMethod ? _self.paymentMethod : paymentMethod // ignore: cast_nullable_to_non_nullable
as int,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as DateTime,items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailItemDto>,settlements: null == settlements ? _self._settlements : settlements // ignore: cast_nullable_to_non_nullable
as List<SaleDetailSettlementDto>,discounts: null == discounts ? _self._discounts : discounts // ignore: cast_nullable_to_non_nullable
as List<SaleDetailDiscountDto>,returns: null == returns ? _self._returns : returns // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnDto>,redemptions: null == redemptions ? _self._redemptions : redemptions // ignore: cast_nullable_to_non_nullable
as List<SaleDetailRedemptionDto>,warnings: null == warnings ? _self._warnings : warnings // ignore: cast_nullable_to_non_nullable
as List<String>,paidAmount: null == paidAmount ? _self.paidAmount : paidAmount // ignore: cast_nullable_to_non_nullable
as double,dueAmount: null == dueAmount ? _self.dueAmount : dueAmount // ignore: cast_nullable_to_non_nullable
as double,totalBeforeDiscount: null == totalBeforeDiscount ? _self.totalBeforeDiscount : totalBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,totalDiscountAmount: null == totalDiscountAmount ? _self.totalDiscountAmount : totalDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,creditNoteAppliedAmount: null == creditNoteAppliedAmount ? _self.creditNoteAppliedAmount : creditNoteAppliedAmount // ignore: cast_nullable_to_non_nullable
as double,status: freezed == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String?,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$SaleDetailItemDto {

@JsonKey(name: 'saleItemId') String get itemId;@JsonKey(name: 'itemName') String get name;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'salesPrice') double get rate;@JsonKey(name: 'taxRatePercent') double get tax;@JsonKey(name: 'totalAmount') double get total;
/// Create a copy of SaleDetailItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailItemDtoCopyWith<SaleDetailItemDto> get copyWith => _$SaleDetailItemDtoCopyWithImpl<SaleDetailItemDto>(this as SaleDetailItemDto, _$identity);

  /// Serializes this SaleDetailItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailItemDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.name, name) || other.name == name)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.rate, rate) || other.rate == rate)&&(identical(other.tax, tax) || other.tax == tax)&&(identical(other.total, total) || other.total == total));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,name,quantity,rate,tax,total);

@override
String toString() {
  return 'SaleDetailItemDto(itemId: $itemId, name: $name, quantity: $quantity, rate: $rate, tax: $tax, total: $total)';
}


}

/// @nodoc
abstract mixin class $SaleDetailItemDtoCopyWith<$Res>  {
  factory $SaleDetailItemDtoCopyWith(SaleDetailItemDto value, $Res Function(SaleDetailItemDto) _then) = _$SaleDetailItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleItemId') String itemId,@JsonKey(name: 'itemName') String name,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'salesPrice') double rate,@JsonKey(name: 'taxRatePercent') double tax,@JsonKey(name: 'totalAmount') double total
});




}
/// @nodoc
class _$SaleDetailItemDtoCopyWithImpl<$Res>
    implements $SaleDetailItemDtoCopyWith<$Res> {
  _$SaleDetailItemDtoCopyWithImpl(this._self, this._then);

  final SaleDetailItemDto _self;
  final $Res Function(SaleDetailItemDto) _then;

/// Create a copy of SaleDetailItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? itemId = null,Object? name = null,Object? quantity = null,Object? rate = null,Object? tax = null,Object? total = null,}) {
  return _then(_self.copyWith(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,rate: null == rate ? _self.rate : rate // ignore: cast_nullable_to_non_nullable
as double,tax: null == tax ? _self.tax : tax // ignore: cast_nullable_to_non_nullable
as double,total: null == total ? _self.total : total // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailItemDto].
extension SaleDetailItemDtoPatterns on SaleDetailItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailItemDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleItemId')  String itemId, @JsonKey(name: 'itemName')  String name, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double rate, @JsonKey(name: 'taxRatePercent')  double tax, @JsonKey(name: 'totalAmount')  double total)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailItemDto() when $default != null:
return $default(_that.itemId,_that.name,_that.quantity,_that.rate,_that.tax,_that.total);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleItemId')  String itemId, @JsonKey(name: 'itemName')  String name, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double rate, @JsonKey(name: 'taxRatePercent')  double tax, @JsonKey(name: 'totalAmount')  double total)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailItemDto():
return $default(_that.itemId,_that.name,_that.quantity,_that.rate,_that.tax,_that.total);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleItemId')  String itemId, @JsonKey(name: 'itemName')  String name, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double rate, @JsonKey(name: 'taxRatePercent')  double tax, @JsonKey(name: 'totalAmount')  double total)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailItemDto() when $default != null:
return $default(_that.itemId,_that.name,_that.quantity,_that.rate,_that.tax,_that.total);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailItemDto implements SaleDetailItemDto {
  const _SaleDetailItemDto({@JsonKey(name: 'saleItemId') required this.itemId, @JsonKey(name: 'itemName') required this.name, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'salesPrice') required this.rate, @JsonKey(name: 'taxRatePercent') required this.tax, @JsonKey(name: 'totalAmount') required this.total});
  factory _SaleDetailItemDto.fromJson(Map<String, dynamic> json) => _$SaleDetailItemDtoFromJson(json);

@override@JsonKey(name: 'saleItemId') final  String itemId;
@override@JsonKey(name: 'itemName') final  String name;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'salesPrice') final  double rate;
@override@JsonKey(name: 'taxRatePercent') final  double tax;
@override@JsonKey(name: 'totalAmount') final  double total;

/// Create a copy of SaleDetailItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailItemDtoCopyWith<_SaleDetailItemDto> get copyWith => __$SaleDetailItemDtoCopyWithImpl<_SaleDetailItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailItemDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.name, name) || other.name == name)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.rate, rate) || other.rate == rate)&&(identical(other.tax, tax) || other.tax == tax)&&(identical(other.total, total) || other.total == total));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,name,quantity,rate,tax,total);

@override
String toString() {
  return 'SaleDetailItemDto(itemId: $itemId, name: $name, quantity: $quantity, rate: $rate, tax: $tax, total: $total)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailItemDtoCopyWith<$Res> implements $SaleDetailItemDtoCopyWith<$Res> {
  factory _$SaleDetailItemDtoCopyWith(_SaleDetailItemDto value, $Res Function(_SaleDetailItemDto) _then) = __$SaleDetailItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleItemId') String itemId,@JsonKey(name: 'itemName') String name,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'salesPrice') double rate,@JsonKey(name: 'taxRatePercent') double tax,@JsonKey(name: 'totalAmount') double total
});




}
/// @nodoc
class __$SaleDetailItemDtoCopyWithImpl<$Res>
    implements _$SaleDetailItemDtoCopyWith<$Res> {
  __$SaleDetailItemDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailItemDto _self;
  final $Res Function(_SaleDetailItemDto) _then;

/// Create a copy of SaleDetailItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? itemId = null,Object? name = null,Object? quantity = null,Object? rate = null,Object? tax = null,Object? total = null,}) {
  return _then(_SaleDetailItemDto(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,rate: null == rate ? _self.rate : rate // ignore: cast_nullable_to_non_nullable
as double,tax: null == tax ? _self.tax : tax // ignore: cast_nullable_to_non_nullable
as double,total: null == total ? _self.total : total // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$SaleDetailSettlementDto {

@JsonKey(name: 'settlementId') String get settlementId;@JsonKey(name: 'method') String get method;@JsonKey(name: 'amount') double get amount;@JsonKey(name: 'settledAt') DateTime get settledAt;
/// Create a copy of SaleDetailSettlementDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailSettlementDtoCopyWith<SaleDetailSettlementDto> get copyWith => _$SaleDetailSettlementDtoCopyWithImpl<SaleDetailSettlementDto>(this as SaleDetailSettlementDto, _$identity);

  /// Serializes this SaleDetailSettlementDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailSettlementDto&&(identical(other.settlementId, settlementId) || other.settlementId == settlementId)&&(identical(other.method, method) || other.method == method)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.settledAt, settledAt) || other.settledAt == settledAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,settlementId,method,amount,settledAt);

@override
String toString() {
  return 'SaleDetailSettlementDto(settlementId: $settlementId, method: $method, amount: $amount, settledAt: $settledAt)';
}


}

/// @nodoc
abstract mixin class $SaleDetailSettlementDtoCopyWith<$Res>  {
  factory $SaleDetailSettlementDtoCopyWith(SaleDetailSettlementDto value, $Res Function(SaleDetailSettlementDto) _then) = _$SaleDetailSettlementDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'settlementId') String settlementId,@JsonKey(name: 'method') String method,@JsonKey(name: 'amount') double amount,@JsonKey(name: 'settledAt') DateTime settledAt
});




}
/// @nodoc
class _$SaleDetailSettlementDtoCopyWithImpl<$Res>
    implements $SaleDetailSettlementDtoCopyWith<$Res> {
  _$SaleDetailSettlementDtoCopyWithImpl(this._self, this._then);

  final SaleDetailSettlementDto _self;
  final $Res Function(SaleDetailSettlementDto) _then;

/// Create a copy of SaleDetailSettlementDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? settlementId = null,Object? method = null,Object? amount = null,Object? settledAt = null,}) {
  return _then(_self.copyWith(
settlementId: null == settlementId ? _self.settlementId : settlementId // ignore: cast_nullable_to_non_nullable
as String,method: null == method ? _self.method : method // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,settledAt: null == settledAt ? _self.settledAt : settledAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailSettlementDto].
extension SaleDetailSettlementDtoPatterns on SaleDetailSettlementDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailSettlementDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailSettlementDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailSettlementDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailSettlementDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailSettlementDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailSettlementDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'settlementId')  String settlementId, @JsonKey(name: 'method')  String method, @JsonKey(name: 'amount')  double amount, @JsonKey(name: 'settledAt')  DateTime settledAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailSettlementDto() when $default != null:
return $default(_that.settlementId,_that.method,_that.amount,_that.settledAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'settlementId')  String settlementId, @JsonKey(name: 'method')  String method, @JsonKey(name: 'amount')  double amount, @JsonKey(name: 'settledAt')  DateTime settledAt)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailSettlementDto():
return $default(_that.settlementId,_that.method,_that.amount,_that.settledAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'settlementId')  String settlementId, @JsonKey(name: 'method')  String method, @JsonKey(name: 'amount')  double amount, @JsonKey(name: 'settledAt')  DateTime settledAt)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailSettlementDto() when $default != null:
return $default(_that.settlementId,_that.method,_that.amount,_that.settledAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailSettlementDto implements SaleDetailSettlementDto {
  const _SaleDetailSettlementDto({@JsonKey(name: 'settlementId') required this.settlementId, @JsonKey(name: 'method') required this.method, @JsonKey(name: 'amount') required this.amount, @JsonKey(name: 'settledAt') required this.settledAt});
  factory _SaleDetailSettlementDto.fromJson(Map<String, dynamic> json) => _$SaleDetailSettlementDtoFromJson(json);

@override@JsonKey(name: 'settlementId') final  String settlementId;
@override@JsonKey(name: 'method') final  String method;
@override@JsonKey(name: 'amount') final  double amount;
@override@JsonKey(name: 'settledAt') final  DateTime settledAt;

/// Create a copy of SaleDetailSettlementDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailSettlementDtoCopyWith<_SaleDetailSettlementDto> get copyWith => __$SaleDetailSettlementDtoCopyWithImpl<_SaleDetailSettlementDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailSettlementDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailSettlementDto&&(identical(other.settlementId, settlementId) || other.settlementId == settlementId)&&(identical(other.method, method) || other.method == method)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.settledAt, settledAt) || other.settledAt == settledAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,settlementId,method,amount,settledAt);

@override
String toString() {
  return 'SaleDetailSettlementDto(settlementId: $settlementId, method: $method, amount: $amount, settledAt: $settledAt)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailSettlementDtoCopyWith<$Res> implements $SaleDetailSettlementDtoCopyWith<$Res> {
  factory _$SaleDetailSettlementDtoCopyWith(_SaleDetailSettlementDto value, $Res Function(_SaleDetailSettlementDto) _then) = __$SaleDetailSettlementDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'settlementId') String settlementId,@JsonKey(name: 'method') String method,@JsonKey(name: 'amount') double amount,@JsonKey(name: 'settledAt') DateTime settledAt
});




}
/// @nodoc
class __$SaleDetailSettlementDtoCopyWithImpl<$Res>
    implements _$SaleDetailSettlementDtoCopyWith<$Res> {
  __$SaleDetailSettlementDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailSettlementDto _self;
  final $Res Function(_SaleDetailSettlementDto) _then;

/// Create a copy of SaleDetailSettlementDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? settlementId = null,Object? method = null,Object? amount = null,Object? settledAt = null,}) {
  return _then(_SaleDetailSettlementDto(
settlementId: null == settlementId ? _self.settlementId : settlementId // ignore: cast_nullable_to_non_nullable
as String,method: null == method ? _self.method : method // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,settledAt: null == settledAt ? _self.settledAt : settledAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}


/// @nodoc
mixin _$SaleDetailDiscountDto {

@JsonKey(name: 'discountId') String get discountId;@JsonKey(name: 'type') String get type;@JsonKey(name: 'value') String get value;@JsonKey(name: 'amount') double get amount;
/// Create a copy of SaleDetailDiscountDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailDiscountDtoCopyWith<SaleDetailDiscountDto> get copyWith => _$SaleDetailDiscountDtoCopyWithImpl<SaleDetailDiscountDto>(this as SaleDetailDiscountDto, _$identity);

  /// Serializes this SaleDetailDiscountDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailDiscountDto&&(identical(other.discountId, discountId) || other.discountId == discountId)&&(identical(other.type, type) || other.type == type)&&(identical(other.value, value) || other.value == value)&&(identical(other.amount, amount) || other.amount == amount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,discountId,type,value,amount);

@override
String toString() {
  return 'SaleDetailDiscountDto(discountId: $discountId, type: $type, value: $value, amount: $amount)';
}


}

/// @nodoc
abstract mixin class $SaleDetailDiscountDtoCopyWith<$Res>  {
  factory $SaleDetailDiscountDtoCopyWith(SaleDetailDiscountDto value, $Res Function(SaleDetailDiscountDto) _then) = _$SaleDetailDiscountDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'discountId') String discountId,@JsonKey(name: 'type') String type,@JsonKey(name: 'value') String value,@JsonKey(name: 'amount') double amount
});




}
/// @nodoc
class _$SaleDetailDiscountDtoCopyWithImpl<$Res>
    implements $SaleDetailDiscountDtoCopyWith<$Res> {
  _$SaleDetailDiscountDtoCopyWithImpl(this._self, this._then);

  final SaleDetailDiscountDto _self;
  final $Res Function(SaleDetailDiscountDto) _then;

/// Create a copy of SaleDetailDiscountDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? discountId = null,Object? type = null,Object? value = null,Object? amount = null,}) {
  return _then(_self.copyWith(
discountId: null == discountId ? _self.discountId : discountId // ignore: cast_nullable_to_non_nullable
as String,type: null == type ? _self.type : type // ignore: cast_nullable_to_non_nullable
as String,value: null == value ? _self.value : value // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailDiscountDto].
extension SaleDetailDiscountDtoPatterns on SaleDetailDiscountDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailDiscountDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailDiscountDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailDiscountDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailDiscountDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailDiscountDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailDiscountDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'discountId')  String discountId, @JsonKey(name: 'type')  String type, @JsonKey(name: 'value')  String value, @JsonKey(name: 'amount')  double amount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailDiscountDto() when $default != null:
return $default(_that.discountId,_that.type,_that.value,_that.amount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'discountId')  String discountId, @JsonKey(name: 'type')  String type, @JsonKey(name: 'value')  String value, @JsonKey(name: 'amount')  double amount)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailDiscountDto():
return $default(_that.discountId,_that.type,_that.value,_that.amount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'discountId')  String discountId, @JsonKey(name: 'type')  String type, @JsonKey(name: 'value')  String value, @JsonKey(name: 'amount')  double amount)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailDiscountDto() when $default != null:
return $default(_that.discountId,_that.type,_that.value,_that.amount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailDiscountDto implements SaleDetailDiscountDto {
  const _SaleDetailDiscountDto({@JsonKey(name: 'discountId') required this.discountId, @JsonKey(name: 'type') required this.type, @JsonKey(name: 'value') required this.value, @JsonKey(name: 'amount') required this.amount});
  factory _SaleDetailDiscountDto.fromJson(Map<String, dynamic> json) => _$SaleDetailDiscountDtoFromJson(json);

@override@JsonKey(name: 'discountId') final  String discountId;
@override@JsonKey(name: 'type') final  String type;
@override@JsonKey(name: 'value') final  String value;
@override@JsonKey(name: 'amount') final  double amount;

/// Create a copy of SaleDetailDiscountDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailDiscountDtoCopyWith<_SaleDetailDiscountDto> get copyWith => __$SaleDetailDiscountDtoCopyWithImpl<_SaleDetailDiscountDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailDiscountDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailDiscountDto&&(identical(other.discountId, discountId) || other.discountId == discountId)&&(identical(other.type, type) || other.type == type)&&(identical(other.value, value) || other.value == value)&&(identical(other.amount, amount) || other.amount == amount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,discountId,type,value,amount);

@override
String toString() {
  return 'SaleDetailDiscountDto(discountId: $discountId, type: $type, value: $value, amount: $amount)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailDiscountDtoCopyWith<$Res> implements $SaleDetailDiscountDtoCopyWith<$Res> {
  factory _$SaleDetailDiscountDtoCopyWith(_SaleDetailDiscountDto value, $Res Function(_SaleDetailDiscountDto) _then) = __$SaleDetailDiscountDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'discountId') String discountId,@JsonKey(name: 'type') String type,@JsonKey(name: 'value') String value,@JsonKey(name: 'amount') double amount
});




}
/// @nodoc
class __$SaleDetailDiscountDtoCopyWithImpl<$Res>
    implements _$SaleDetailDiscountDtoCopyWith<$Res> {
  __$SaleDetailDiscountDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailDiscountDto _self;
  final $Res Function(_SaleDetailDiscountDto) _then;

/// Create a copy of SaleDetailDiscountDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? discountId = null,Object? type = null,Object? value = null,Object? amount = null,}) {
  return _then(_SaleDetailDiscountDto(
discountId: null == discountId ? _self.discountId : discountId // ignore: cast_nullable_to_non_nullable
as String,type: null == type ? _self.type : type // ignore: cast_nullable_to_non_nullable
as String,value: null == value ? _self.value : value // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$SaleDetailReturnDto {

@JsonKey(name: 'saleReturnId') String get returnId;@JsonKey(name: 'returnNumber') String get returnNumber;@JsonKey(name: 'items') List<SaleDetailReturnItemDto> get items;@JsonKey(name: 'totalRefundAmount') double get amount;@JsonKey(name: 'processedAt') DateTime get returnedAt;
/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailReturnDtoCopyWith<SaleDetailReturnDto> get copyWith => _$SaleDetailReturnDtoCopyWithImpl<SaleDetailReturnDto>(this as SaleDetailReturnDto, _$identity);

  /// Serializes this SaleDetailReturnDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailReturnDto&&(identical(other.returnId, returnId) || other.returnId == returnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.returnedAt, returnedAt) || other.returnedAt == returnedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,returnId,returnNumber,const DeepCollectionEquality().hash(items),amount,returnedAt);

@override
String toString() {
  return 'SaleDetailReturnDto(returnId: $returnId, returnNumber: $returnNumber, items: $items, amount: $amount, returnedAt: $returnedAt)';
}


}

/// @nodoc
abstract mixin class $SaleDetailReturnDtoCopyWith<$Res>  {
  factory $SaleDetailReturnDtoCopyWith(SaleDetailReturnDto value, $Res Function(SaleDetailReturnDto) _then) = _$SaleDetailReturnDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleReturnId') String returnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'items') List<SaleDetailReturnItemDto> items,@JsonKey(name: 'totalRefundAmount') double amount,@JsonKey(name: 'processedAt') DateTime returnedAt
});




}
/// @nodoc
class _$SaleDetailReturnDtoCopyWithImpl<$Res>
    implements $SaleDetailReturnDtoCopyWith<$Res> {
  _$SaleDetailReturnDtoCopyWithImpl(this._self, this._then);

  final SaleDetailReturnDto _self;
  final $Res Function(SaleDetailReturnDto) _then;

/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? returnId = null,Object? returnNumber = null,Object? items = null,Object? amount = null,Object? returnedAt = null,}) {
  return _then(_self.copyWith(
returnId: null == returnId ? _self.returnId : returnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnItemDto>,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,returnedAt: null == returnedAt ? _self.returnedAt : returnedAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailReturnDto].
extension SaleDetailReturnDtoPatterns on SaleDetailReturnDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailReturnDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailReturnDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailReturnDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailReturnDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailReturnDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailReturnDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleReturnId')  String returnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'items')  List<SaleDetailReturnItemDto> items, @JsonKey(name: 'totalRefundAmount')  double amount, @JsonKey(name: 'processedAt')  DateTime returnedAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailReturnDto() when $default != null:
return $default(_that.returnId,_that.returnNumber,_that.items,_that.amount,_that.returnedAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleReturnId')  String returnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'items')  List<SaleDetailReturnItemDto> items, @JsonKey(name: 'totalRefundAmount')  double amount, @JsonKey(name: 'processedAt')  DateTime returnedAt)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnDto():
return $default(_that.returnId,_that.returnNumber,_that.items,_that.amount,_that.returnedAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleReturnId')  String returnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'items')  List<SaleDetailReturnItemDto> items, @JsonKey(name: 'totalRefundAmount')  double amount, @JsonKey(name: 'processedAt')  DateTime returnedAt)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnDto() when $default != null:
return $default(_that.returnId,_that.returnNumber,_that.items,_that.amount,_that.returnedAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailReturnDto implements SaleDetailReturnDto {
  const _SaleDetailReturnDto({@JsonKey(name: 'saleReturnId') required this.returnId, @JsonKey(name: 'returnNumber') required this.returnNumber, @JsonKey(name: 'items') final  List<SaleDetailReturnItemDto> items = const [], @JsonKey(name: 'totalRefundAmount') required this.amount, @JsonKey(name: 'processedAt') required this.returnedAt}): _items = items;
  factory _SaleDetailReturnDto.fromJson(Map<String, dynamic> json) => _$SaleDetailReturnDtoFromJson(json);

@override@JsonKey(name: 'saleReturnId') final  String returnId;
@override@JsonKey(name: 'returnNumber') final  String returnNumber;
 final  List<SaleDetailReturnItemDto> _items;
@override@JsonKey(name: 'items') List<SaleDetailReturnItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override@JsonKey(name: 'totalRefundAmount') final  double amount;
@override@JsonKey(name: 'processedAt') final  DateTime returnedAt;

/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailReturnDtoCopyWith<_SaleDetailReturnDto> get copyWith => __$SaleDetailReturnDtoCopyWithImpl<_SaleDetailReturnDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailReturnDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailReturnDto&&(identical(other.returnId, returnId) || other.returnId == returnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.returnedAt, returnedAt) || other.returnedAt == returnedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,returnId,returnNumber,const DeepCollectionEquality().hash(_items),amount,returnedAt);

@override
String toString() {
  return 'SaleDetailReturnDto(returnId: $returnId, returnNumber: $returnNumber, items: $items, amount: $amount, returnedAt: $returnedAt)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailReturnDtoCopyWith<$Res> implements $SaleDetailReturnDtoCopyWith<$Res> {
  factory _$SaleDetailReturnDtoCopyWith(_SaleDetailReturnDto value, $Res Function(_SaleDetailReturnDto) _then) = __$SaleDetailReturnDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleReturnId') String returnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'items') List<SaleDetailReturnItemDto> items,@JsonKey(name: 'totalRefundAmount') double amount,@JsonKey(name: 'processedAt') DateTime returnedAt
});




}
/// @nodoc
class __$SaleDetailReturnDtoCopyWithImpl<$Res>
    implements _$SaleDetailReturnDtoCopyWith<$Res> {
  __$SaleDetailReturnDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailReturnDto _self;
  final $Res Function(_SaleDetailReturnDto) _then;

/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? returnId = null,Object? returnNumber = null,Object? items = null,Object? amount = null,Object? returnedAt = null,}) {
  return _then(_SaleDetailReturnDto(
returnId: null == returnId ? _self.returnId : returnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnItemDto>,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,returnedAt: null == returnedAt ? _self.returnedAt : returnedAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}


/// @nodoc
mixin _$SaleDetailReturnItemDto {

@JsonKey(name: 'saleItemId') String get itemId;@JsonKey(name: 'itemName') String? get itemName;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'approvedRefundAmount') double get amount;
/// Create a copy of SaleDetailReturnItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailReturnItemDtoCopyWith<SaleDetailReturnItemDto> get copyWith => _$SaleDetailReturnItemDtoCopyWithImpl<SaleDetailReturnItemDto>(this as SaleDetailReturnItemDto, _$identity);

  /// Serializes this SaleDetailReturnItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailReturnItemDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.amount, amount) || other.amount == amount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,itemName,quantity,amount);

@override
String toString() {
  return 'SaleDetailReturnItemDto(itemId: $itemId, itemName: $itemName, quantity: $quantity, amount: $amount)';
}


}

/// @nodoc
abstract mixin class $SaleDetailReturnItemDtoCopyWith<$Res>  {
  factory $SaleDetailReturnItemDtoCopyWith(SaleDetailReturnItemDto value, $Res Function(SaleDetailReturnItemDto) _then) = _$SaleDetailReturnItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleItemId') String itemId,@JsonKey(name: 'itemName') String? itemName,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'approvedRefundAmount') double amount
});




}
/// @nodoc
class _$SaleDetailReturnItemDtoCopyWithImpl<$Res>
    implements $SaleDetailReturnItemDtoCopyWith<$Res> {
  _$SaleDetailReturnItemDtoCopyWithImpl(this._self, this._then);

  final SaleDetailReturnItemDto _self;
  final $Res Function(SaleDetailReturnItemDto) _then;

/// Create a copy of SaleDetailReturnItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? itemId = null,Object? itemName = freezed,Object? quantity = null,Object? amount = null,}) {
  return _then(_self.copyWith(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: freezed == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String?,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailReturnItemDto].
extension SaleDetailReturnItemDtoPatterns on SaleDetailReturnItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailReturnItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailReturnItemDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailReturnItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleItemId')  String itemId, @JsonKey(name: 'itemName')  String? itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'approvedRefundAmount')  double amount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto() when $default != null:
return $default(_that.itemId,_that.itemName,_that.quantity,_that.amount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleItemId')  String itemId, @JsonKey(name: 'itemName')  String? itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'approvedRefundAmount')  double amount)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto():
return $default(_that.itemId,_that.itemName,_that.quantity,_that.amount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleItemId')  String itemId, @JsonKey(name: 'itemName')  String? itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'approvedRefundAmount')  double amount)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto() when $default != null:
return $default(_that.itemId,_that.itemName,_that.quantity,_that.amount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailReturnItemDto implements SaleDetailReturnItemDto {
  const _SaleDetailReturnItemDto({@JsonKey(name: 'saleItemId') required this.itemId, @JsonKey(name: 'itemName') this.itemName, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'approvedRefundAmount') required this.amount});
  factory _SaleDetailReturnItemDto.fromJson(Map<String, dynamic> json) => _$SaleDetailReturnItemDtoFromJson(json);

@override@JsonKey(name: 'saleItemId') final  String itemId;
@override@JsonKey(name: 'itemName') final  String? itemName;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'approvedRefundAmount') final  double amount;

/// Create a copy of SaleDetailReturnItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailReturnItemDtoCopyWith<_SaleDetailReturnItemDto> get copyWith => __$SaleDetailReturnItemDtoCopyWithImpl<_SaleDetailReturnItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailReturnItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailReturnItemDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.amount, amount) || other.amount == amount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,itemName,quantity,amount);

@override
String toString() {
  return 'SaleDetailReturnItemDto(itemId: $itemId, itemName: $itemName, quantity: $quantity, amount: $amount)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailReturnItemDtoCopyWith<$Res> implements $SaleDetailReturnItemDtoCopyWith<$Res> {
  factory _$SaleDetailReturnItemDtoCopyWith(_SaleDetailReturnItemDto value, $Res Function(_SaleDetailReturnItemDto) _then) = __$SaleDetailReturnItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleItemId') String itemId,@JsonKey(name: 'itemName') String? itemName,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'approvedRefundAmount') double amount
});




}
/// @nodoc
class __$SaleDetailReturnItemDtoCopyWithImpl<$Res>
    implements _$SaleDetailReturnItemDtoCopyWith<$Res> {
  __$SaleDetailReturnItemDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailReturnItemDto _self;
  final $Res Function(_SaleDetailReturnItemDto) _then;

/// Create a copy of SaleDetailReturnItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? itemId = null,Object? itemName = freezed,Object? quantity = null,Object? amount = null,}) {
  return _then(_SaleDetailReturnItemDto(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: freezed == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String?,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$SaleDetailRedemptionDto {

@JsonKey(name: 'creditNoteId') String get redemptionId;@JsonKey(name: 'code') String get code;@JsonKey(name: 'appliedAmount') double get amount;
/// Create a copy of SaleDetailRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailRedemptionDtoCopyWith<SaleDetailRedemptionDto> get copyWith => _$SaleDetailRedemptionDtoCopyWithImpl<SaleDetailRedemptionDto>(this as SaleDetailRedemptionDto, _$identity);

  /// Serializes this SaleDetailRedemptionDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailRedemptionDto&&(identical(other.redemptionId, redemptionId) || other.redemptionId == redemptionId)&&(identical(other.code, code) || other.code == code)&&(identical(other.amount, amount) || other.amount == amount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,redemptionId,code,amount);

@override
String toString() {
  return 'SaleDetailRedemptionDto(redemptionId: $redemptionId, code: $code, amount: $amount)';
}


}

/// @nodoc
abstract mixin class $SaleDetailRedemptionDtoCopyWith<$Res>  {
  factory $SaleDetailRedemptionDtoCopyWith(SaleDetailRedemptionDto value, $Res Function(SaleDetailRedemptionDto) _then) = _$SaleDetailRedemptionDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'creditNoteId') String redemptionId,@JsonKey(name: 'code') String code,@JsonKey(name: 'appliedAmount') double amount
});




}
/// @nodoc
class _$SaleDetailRedemptionDtoCopyWithImpl<$Res>
    implements $SaleDetailRedemptionDtoCopyWith<$Res> {
  _$SaleDetailRedemptionDtoCopyWithImpl(this._self, this._then);

  final SaleDetailRedemptionDto _self;
  final $Res Function(SaleDetailRedemptionDto) _then;

/// Create a copy of SaleDetailRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? redemptionId = null,Object? code = null,Object? amount = null,}) {
  return _then(_self.copyWith(
redemptionId: null == redemptionId ? _self.redemptionId : redemptionId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailRedemptionDto].
extension SaleDetailRedemptionDtoPatterns on SaleDetailRedemptionDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailRedemptionDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailRedemptionDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailRedemptionDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailRedemptionDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailRedemptionDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailRedemptionDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String redemptionId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'appliedAmount')  double amount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailRedemptionDto() when $default != null:
return $default(_that.redemptionId,_that.code,_that.amount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String redemptionId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'appliedAmount')  double amount)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailRedemptionDto():
return $default(_that.redemptionId,_that.code,_that.amount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'creditNoteId')  String redemptionId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'appliedAmount')  double amount)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailRedemptionDto() when $default != null:
return $default(_that.redemptionId,_that.code,_that.amount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailRedemptionDto implements SaleDetailRedemptionDto {
  const _SaleDetailRedemptionDto({@JsonKey(name: 'creditNoteId') required this.redemptionId, @JsonKey(name: 'code') required this.code, @JsonKey(name: 'appliedAmount') required this.amount});
  factory _SaleDetailRedemptionDto.fromJson(Map<String, dynamic> json) => _$SaleDetailRedemptionDtoFromJson(json);

@override@JsonKey(name: 'creditNoteId') final  String redemptionId;
@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'appliedAmount') final  double amount;

/// Create a copy of SaleDetailRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailRedemptionDtoCopyWith<_SaleDetailRedemptionDto> get copyWith => __$SaleDetailRedemptionDtoCopyWithImpl<_SaleDetailRedemptionDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailRedemptionDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailRedemptionDto&&(identical(other.redemptionId, redemptionId) || other.redemptionId == redemptionId)&&(identical(other.code, code) || other.code == code)&&(identical(other.amount, amount) || other.amount == amount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,redemptionId,code,amount);

@override
String toString() {
  return 'SaleDetailRedemptionDto(redemptionId: $redemptionId, code: $code, amount: $amount)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailRedemptionDtoCopyWith<$Res> implements $SaleDetailRedemptionDtoCopyWith<$Res> {
  factory _$SaleDetailRedemptionDtoCopyWith(_SaleDetailRedemptionDto value, $Res Function(_SaleDetailRedemptionDto) _then) = __$SaleDetailRedemptionDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'creditNoteId') String redemptionId,@JsonKey(name: 'code') String code,@JsonKey(name: 'appliedAmount') double amount
});




}
/// @nodoc
class __$SaleDetailRedemptionDtoCopyWithImpl<$Res>
    implements _$SaleDetailRedemptionDtoCopyWith<$Res> {
  __$SaleDetailRedemptionDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailRedemptionDto _self;
  final $Res Function(_SaleDetailRedemptionDto) _then;

/// Create a copy of SaleDetailRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? redemptionId = null,Object? code = null,Object? amount = null,}) {
  return _then(_SaleDetailRedemptionDto(
redemptionId: null == redemptionId ? _self.redemptionId : redemptionId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
