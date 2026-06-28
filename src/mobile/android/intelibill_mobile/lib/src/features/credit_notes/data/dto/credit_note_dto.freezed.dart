// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'credit_note_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CreditNoteListItemDto {

@JsonKey(name: 'creditNoteId') String get creditNoteId;@JsonKey(name: 'code') String get code;@JsonKey(name: 'status') String get status;@JsonKey(name: 'originalAmount') double get originalAmount;@JsonKey(name: 'availableBalance') double get availableBalance;@JsonKey(name: 'expiresAt') DateTime? get expiresAt;@JsonKey(name: 'issuedAt') DateTime get issuedAt;@JsonKey(name: 'saleReturnId') String get saleReturnId;@JsonKey(name: 'returnNumber') String get returnNumber;@JsonKey(name: 'saleId') String get saleId;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'customerName') String? get customerName;
/// Create a copy of CreditNoteListItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreditNoteListItemDtoCopyWith<CreditNoteListItemDto> get copyWith => _$CreditNoteListItemDtoCopyWithImpl<CreditNoteListItemDto>(this as CreditNoteListItemDto, _$identity);

  /// Serializes this CreditNoteListItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreditNoteListItemDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.status, status) || other.status == status)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.issuedAt, issuedAt) || other.issuedAt == issuedAt)&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerName, customerName) || other.customerName == customerName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,status,originalAmount,availableBalance,expiresAt,issuedAt,saleReturnId,returnNumber,saleId,invoiceNumber,customerName);

@override
String toString() {
  return 'CreditNoteListItemDto(creditNoteId: $creditNoteId, code: $code, status: $status, originalAmount: $originalAmount, availableBalance: $availableBalance, expiresAt: $expiresAt, issuedAt: $issuedAt, saleReturnId: $saleReturnId, returnNumber: $returnNumber, saleId: $saleId, invoiceNumber: $invoiceNumber, customerName: $customerName)';
}


}

/// @nodoc
abstract mixin class $CreditNoteListItemDtoCopyWith<$Res>  {
  factory $CreditNoteListItemDtoCopyWith(CreditNoteListItemDto value, $Res Function(CreditNoteListItemDto) _then) = _$CreditNoteListItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'status') String status,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'issuedAt') DateTime issuedAt,@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerName') String? customerName
});




}
/// @nodoc
class _$CreditNoteListItemDtoCopyWithImpl<$Res>
    implements $CreditNoteListItemDtoCopyWith<$Res> {
  _$CreditNoteListItemDtoCopyWithImpl(this._self, this._then);

  final CreditNoteListItemDto _self;
  final $Res Function(CreditNoteListItemDto) _then;

/// Create a copy of CreditNoteListItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? creditNoteId = null,Object? code = null,Object? status = null,Object? originalAmount = null,Object? availableBalance = null,Object? expiresAt = freezed,Object? issuedAt = null,Object? saleReturnId = null,Object? returnNumber = null,Object? saleId = null,Object? invoiceNumber = null,Object? customerName = freezed,}) {
  return _then(_self.copyWith(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,issuedAt: null == issuedAt ? _self.issuedAt : issuedAt // ignore: cast_nullable_to_non_nullable
as DateTime,saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [CreditNoteListItemDto].
extension CreditNoteListItemDtoPatterns on CreditNoteListItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreditNoteListItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreditNoteListItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreditNoteListItemDto value)  $default,){
final _that = this;
switch (_that) {
case _CreditNoteListItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreditNoteListItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreditNoteListItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'issuedAt')  DateTime issuedAt, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerName')  String? customerName)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreditNoteListItemDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.status,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.issuedAt,_that.saleReturnId,_that.returnNumber,_that.saleId,_that.invoiceNumber,_that.customerName);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'issuedAt')  DateTime issuedAt, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerName')  String? customerName)  $default,) {final _that = this;
switch (_that) {
case _CreditNoteListItemDto():
return $default(_that.creditNoteId,_that.code,_that.status,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.issuedAt,_that.saleReturnId,_that.returnNumber,_that.saleId,_that.invoiceNumber,_that.customerName);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'issuedAt')  DateTime issuedAt, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerName')  String? customerName)?  $default,) {final _that = this;
switch (_that) {
case _CreditNoteListItemDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.status,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.issuedAt,_that.saleReturnId,_that.returnNumber,_that.saleId,_that.invoiceNumber,_that.customerName);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreditNoteListItemDto implements CreditNoteListItemDto {
  const _CreditNoteListItemDto({@JsonKey(name: 'creditNoteId') required this.creditNoteId, @JsonKey(name: 'code') required this.code, @JsonKey(name: 'status') required this.status, @JsonKey(name: 'originalAmount') required this.originalAmount, @JsonKey(name: 'availableBalance') required this.availableBalance, @JsonKey(name: 'expiresAt') this.expiresAt, @JsonKey(name: 'issuedAt') required this.issuedAt, @JsonKey(name: 'saleReturnId') required this.saleReturnId, @JsonKey(name: 'returnNumber') required this.returnNumber, @JsonKey(name: 'saleId') required this.saleId, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'customerName') this.customerName});
  factory _CreditNoteListItemDto.fromJson(Map<String, dynamic> json) => _$CreditNoteListItemDtoFromJson(json);

@override@JsonKey(name: 'creditNoteId') final  String creditNoteId;
@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'status') final  String status;
@override@JsonKey(name: 'originalAmount') final  double originalAmount;
@override@JsonKey(name: 'availableBalance') final  double availableBalance;
@override@JsonKey(name: 'expiresAt') final  DateTime? expiresAt;
@override@JsonKey(name: 'issuedAt') final  DateTime issuedAt;
@override@JsonKey(name: 'saleReturnId') final  String saleReturnId;
@override@JsonKey(name: 'returnNumber') final  String returnNumber;
@override@JsonKey(name: 'saleId') final  String saleId;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'customerName') final  String? customerName;

/// Create a copy of CreditNoteListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreditNoteListItemDtoCopyWith<_CreditNoteListItemDto> get copyWith => __$CreditNoteListItemDtoCopyWithImpl<_CreditNoteListItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreditNoteListItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreditNoteListItemDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.status, status) || other.status == status)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.issuedAt, issuedAt) || other.issuedAt == issuedAt)&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerName, customerName) || other.customerName == customerName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,status,originalAmount,availableBalance,expiresAt,issuedAt,saleReturnId,returnNumber,saleId,invoiceNumber,customerName);

@override
String toString() {
  return 'CreditNoteListItemDto(creditNoteId: $creditNoteId, code: $code, status: $status, originalAmount: $originalAmount, availableBalance: $availableBalance, expiresAt: $expiresAt, issuedAt: $issuedAt, saleReturnId: $saleReturnId, returnNumber: $returnNumber, saleId: $saleId, invoiceNumber: $invoiceNumber, customerName: $customerName)';
}


}

/// @nodoc
abstract mixin class _$CreditNoteListItemDtoCopyWith<$Res> implements $CreditNoteListItemDtoCopyWith<$Res> {
  factory _$CreditNoteListItemDtoCopyWith(_CreditNoteListItemDto value, $Res Function(_CreditNoteListItemDto) _then) = __$CreditNoteListItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'status') String status,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'issuedAt') DateTime issuedAt,@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerName') String? customerName
});




}
/// @nodoc
class __$CreditNoteListItemDtoCopyWithImpl<$Res>
    implements _$CreditNoteListItemDtoCopyWith<$Res> {
  __$CreditNoteListItemDtoCopyWithImpl(this._self, this._then);

  final _CreditNoteListItemDto _self;
  final $Res Function(_CreditNoteListItemDto) _then;

/// Create a copy of CreditNoteListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? creditNoteId = null,Object? code = null,Object? status = null,Object? originalAmount = null,Object? availableBalance = null,Object? expiresAt = freezed,Object? issuedAt = null,Object? saleReturnId = null,Object? returnNumber = null,Object? saleId = null,Object? invoiceNumber = null,Object? customerName = freezed,}) {
  return _then(_CreditNoteListItemDto(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,issuedAt: null == issuedAt ? _self.issuedAt : issuedAt // ignore: cast_nullable_to_non_nullable
as DateTime,saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}


/// @nodoc
mixin _$CreditNoteDto {

@JsonKey(name: 'creditNoteId') String get creditNoteId;@JsonKey(name: 'code') String get code;@JsonKey(name: 'status') String get status;@JsonKey(name: 'originalAmount') double get originalAmount;@JsonKey(name: 'availableBalance') double get availableBalance;@JsonKey(name: 'expiresAt') DateTime? get expiresAt;@JsonKey(name: 'isVoided') bool get isVoided;@JsonKey(name: 'saleReturnId') String get saleReturnId;@JsonKey(name: 'reason') String get reason;@JsonKey(name: 'voidReason') String? get voidReason;@JsonKey(name: 'returnNumber') String get returnNumber;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'customerName') String? get customerName;
/// Create a copy of CreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreditNoteDtoCopyWith<CreditNoteDto> get copyWith => _$CreditNoteDtoCopyWithImpl<CreditNoteDto>(this as CreditNoteDto, _$identity);

  /// Serializes this CreditNoteDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreditNoteDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.status, status) || other.status == status)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.voidReason, voidReason) || other.voidReason == voidReason)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerName, customerName) || other.customerName == customerName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,status,originalAmount,availableBalance,expiresAt,isVoided,saleReturnId,reason,voidReason,returnNumber,invoiceNumber,customerName);

@override
String toString() {
  return 'CreditNoteDto(creditNoteId: $creditNoteId, code: $code, status: $status, originalAmount: $originalAmount, availableBalance: $availableBalance, expiresAt: $expiresAt, isVoided: $isVoided, saleReturnId: $saleReturnId, reason: $reason, voidReason: $voidReason, returnNumber: $returnNumber, invoiceNumber: $invoiceNumber, customerName: $customerName)';
}


}

/// @nodoc
abstract mixin class $CreditNoteDtoCopyWith<$Res>  {
  factory $CreditNoteDtoCopyWith(CreditNoteDto value, $Res Function(CreditNoteDto) _then) = _$CreditNoteDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'status') String status,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'isVoided') bool isVoided,@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'voidReason') String? voidReason,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerName') String? customerName
});




}
/// @nodoc
class _$CreditNoteDtoCopyWithImpl<$Res>
    implements $CreditNoteDtoCopyWith<$Res> {
  _$CreditNoteDtoCopyWithImpl(this._self, this._then);

  final CreditNoteDto _self;
  final $Res Function(CreditNoteDto) _then;

/// Create a copy of CreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? creditNoteId = null,Object? code = null,Object? status = null,Object? originalAmount = null,Object? availableBalance = null,Object? expiresAt = freezed,Object? isVoided = null,Object? saleReturnId = null,Object? reason = null,Object? voidReason = freezed,Object? returnNumber = null,Object? invoiceNumber = null,Object? customerName = freezed,}) {
  return _then(_self.copyWith(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,voidReason: freezed == voidReason ? _self.voidReason : voidReason // ignore: cast_nullable_to_non_nullable
as String?,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [CreditNoteDto].
extension CreditNoteDtoPatterns on CreditNoteDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreditNoteDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreditNoteDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreditNoteDto value)  $default,){
final _that = this;
switch (_that) {
case _CreditNoteDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreditNoteDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreditNoteDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'voidReason')  String? voidReason, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerName')  String? customerName)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreditNoteDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.status,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.isVoided,_that.saleReturnId,_that.reason,_that.voidReason,_that.returnNumber,_that.invoiceNumber,_that.customerName);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'voidReason')  String? voidReason, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerName')  String? customerName)  $default,) {final _that = this;
switch (_that) {
case _CreditNoteDto():
return $default(_that.creditNoteId,_that.code,_that.status,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.isVoided,_that.saleReturnId,_that.reason,_that.voidReason,_that.returnNumber,_that.invoiceNumber,_that.customerName);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'voidReason')  String? voidReason, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerName')  String? customerName)?  $default,) {final _that = this;
switch (_that) {
case _CreditNoteDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.status,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.isVoided,_that.saleReturnId,_that.reason,_that.voidReason,_that.returnNumber,_that.invoiceNumber,_that.customerName);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreditNoteDto implements CreditNoteDto {
  const _CreditNoteDto({@JsonKey(name: 'creditNoteId') required this.creditNoteId, @JsonKey(name: 'code') required this.code, @JsonKey(name: 'status') required this.status, @JsonKey(name: 'originalAmount') required this.originalAmount, @JsonKey(name: 'availableBalance') required this.availableBalance, @JsonKey(name: 'expiresAt') this.expiresAt, @JsonKey(name: 'isVoided') required this.isVoided, @JsonKey(name: 'saleReturnId') required this.saleReturnId, @JsonKey(name: 'reason') required this.reason, @JsonKey(name: 'voidReason') this.voidReason, @JsonKey(name: 'returnNumber') required this.returnNumber, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'customerName') this.customerName});
  factory _CreditNoteDto.fromJson(Map<String, dynamic> json) => _$CreditNoteDtoFromJson(json);

@override@JsonKey(name: 'creditNoteId') final  String creditNoteId;
@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'status') final  String status;
@override@JsonKey(name: 'originalAmount') final  double originalAmount;
@override@JsonKey(name: 'availableBalance') final  double availableBalance;
@override@JsonKey(name: 'expiresAt') final  DateTime? expiresAt;
@override@JsonKey(name: 'isVoided') final  bool isVoided;
@override@JsonKey(name: 'saleReturnId') final  String saleReturnId;
@override@JsonKey(name: 'reason') final  String reason;
@override@JsonKey(name: 'voidReason') final  String? voidReason;
@override@JsonKey(name: 'returnNumber') final  String returnNumber;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'customerName') final  String? customerName;

/// Create a copy of CreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreditNoteDtoCopyWith<_CreditNoteDto> get copyWith => __$CreditNoteDtoCopyWithImpl<_CreditNoteDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreditNoteDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreditNoteDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.status, status) || other.status == status)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.voidReason, voidReason) || other.voidReason == voidReason)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerName, customerName) || other.customerName == customerName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,status,originalAmount,availableBalance,expiresAt,isVoided,saleReturnId,reason,voidReason,returnNumber,invoiceNumber,customerName);

@override
String toString() {
  return 'CreditNoteDto(creditNoteId: $creditNoteId, code: $code, status: $status, originalAmount: $originalAmount, availableBalance: $availableBalance, expiresAt: $expiresAt, isVoided: $isVoided, saleReturnId: $saleReturnId, reason: $reason, voidReason: $voidReason, returnNumber: $returnNumber, invoiceNumber: $invoiceNumber, customerName: $customerName)';
}


}

/// @nodoc
abstract mixin class _$CreditNoteDtoCopyWith<$Res> implements $CreditNoteDtoCopyWith<$Res> {
  factory _$CreditNoteDtoCopyWith(_CreditNoteDto value, $Res Function(_CreditNoteDto) _then) = __$CreditNoteDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'status') String status,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'isVoided') bool isVoided,@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'voidReason') String? voidReason,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerName') String? customerName
});




}
/// @nodoc
class __$CreditNoteDtoCopyWithImpl<$Res>
    implements _$CreditNoteDtoCopyWith<$Res> {
  __$CreditNoteDtoCopyWithImpl(this._self, this._then);

  final _CreditNoteDto _self;
  final $Res Function(_CreditNoteDto) _then;

/// Create a copy of CreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? creditNoteId = null,Object? code = null,Object? status = null,Object? originalAmount = null,Object? availableBalance = null,Object? expiresAt = freezed,Object? isVoided = null,Object? saleReturnId = null,Object? reason = null,Object? voidReason = freezed,Object? returnNumber = null,Object? invoiceNumber = null,Object? customerName = freezed,}) {
  return _then(_CreditNoteDto(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,voidReason: freezed == voidReason ? _self.voidReason : voidReason // ignore: cast_nullable_to_non_nullable
as String?,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}


/// @nodoc
mixin _$CreditNotePrintDto {

@JsonKey(name: 'creditNoteId') String get creditNoteId;@JsonKey(name: 'code') String get code;@JsonKey(name: 'status') String get status;@JsonKey(name: 'isUsable') bool get isUsable;@JsonKey(name: 'originalAmount') double get originalAmount;@JsonKey(name: 'availableBalance') double get availableBalance;@JsonKey(name: 'issuedAt') DateTime get issuedAt;@JsonKey(name: 'expiresAt') DateTime? get expiresAt;@JsonKey(name: 'saleId') String get saleId;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'saleReturnId') String get saleReturnId;@JsonKey(name: 'returnNumber') String get returnNumber;@JsonKey(name: 'customerDisplayName') String get customerDisplayName;@JsonKey(name: 'reason') String get reason;@JsonKey(name: 'voidReason') String? get voidReason;
/// Create a copy of CreditNotePrintDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreditNotePrintDtoCopyWith<CreditNotePrintDto> get copyWith => _$CreditNotePrintDtoCopyWithImpl<CreditNotePrintDto>(this as CreditNotePrintDto, _$identity);

  /// Serializes this CreditNotePrintDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreditNotePrintDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.status, status) || other.status == status)&&(identical(other.isUsable, isUsable) || other.isUsable == isUsable)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.issuedAt, issuedAt) || other.issuedAt == issuedAt)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.customerDisplayName, customerDisplayName) || other.customerDisplayName == customerDisplayName)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.voidReason, voidReason) || other.voidReason == voidReason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,status,isUsable,originalAmount,availableBalance,issuedAt,expiresAt,saleId,invoiceNumber,saleReturnId,returnNumber,customerDisplayName,reason,voidReason);

@override
String toString() {
  return 'CreditNotePrintDto(creditNoteId: $creditNoteId, code: $code, status: $status, isUsable: $isUsable, originalAmount: $originalAmount, availableBalance: $availableBalance, issuedAt: $issuedAt, expiresAt: $expiresAt, saleId: $saleId, invoiceNumber: $invoiceNumber, saleReturnId: $saleReturnId, returnNumber: $returnNumber, customerDisplayName: $customerDisplayName, reason: $reason, voidReason: $voidReason)';
}


}

/// @nodoc
abstract mixin class $CreditNotePrintDtoCopyWith<$Res>  {
  factory $CreditNotePrintDtoCopyWith(CreditNotePrintDto value, $Res Function(CreditNotePrintDto) _then) = _$CreditNotePrintDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'status') String status,@JsonKey(name: 'isUsable') bool isUsable,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'issuedAt') DateTime issuedAt,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'customerDisplayName') String customerDisplayName,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'voidReason') String? voidReason
});




}
/// @nodoc
class _$CreditNotePrintDtoCopyWithImpl<$Res>
    implements $CreditNotePrintDtoCopyWith<$Res> {
  _$CreditNotePrintDtoCopyWithImpl(this._self, this._then);

  final CreditNotePrintDto _self;
  final $Res Function(CreditNotePrintDto) _then;

/// Create a copy of CreditNotePrintDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? creditNoteId = null,Object? code = null,Object? status = null,Object? isUsable = null,Object? originalAmount = null,Object? availableBalance = null,Object? issuedAt = null,Object? expiresAt = freezed,Object? saleId = null,Object? invoiceNumber = null,Object? saleReturnId = null,Object? returnNumber = null,Object? customerDisplayName = null,Object? reason = null,Object? voidReason = freezed,}) {
  return _then(_self.copyWith(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,isUsable: null == isUsable ? _self.isUsable : isUsable // ignore: cast_nullable_to_non_nullable
as bool,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,issuedAt: null == issuedAt ? _self.issuedAt : issuedAt // ignore: cast_nullable_to_non_nullable
as DateTime,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,customerDisplayName: null == customerDisplayName ? _self.customerDisplayName : customerDisplayName // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,voidReason: freezed == voidReason ? _self.voidReason : voidReason // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [CreditNotePrintDto].
extension CreditNotePrintDtoPatterns on CreditNotePrintDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreditNotePrintDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreditNotePrintDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreditNotePrintDto value)  $default,){
final _that = this;
switch (_that) {
case _CreditNotePrintDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreditNotePrintDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreditNotePrintDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'isUsable')  bool isUsable, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'issuedAt')  DateTime issuedAt, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'customerDisplayName')  String customerDisplayName, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'voidReason')  String? voidReason)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreditNotePrintDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.status,_that.isUsable,_that.originalAmount,_that.availableBalance,_that.issuedAt,_that.expiresAt,_that.saleId,_that.invoiceNumber,_that.saleReturnId,_that.returnNumber,_that.customerDisplayName,_that.reason,_that.voidReason);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'isUsable')  bool isUsable, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'issuedAt')  DateTime issuedAt, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'customerDisplayName')  String customerDisplayName, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'voidReason')  String? voidReason)  $default,) {final _that = this;
switch (_that) {
case _CreditNotePrintDto():
return $default(_that.creditNoteId,_that.code,_that.status,_that.isUsable,_that.originalAmount,_that.availableBalance,_that.issuedAt,_that.expiresAt,_that.saleId,_that.invoiceNumber,_that.saleReturnId,_that.returnNumber,_that.customerDisplayName,_that.reason,_that.voidReason);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'status')  String status, @JsonKey(name: 'isUsable')  bool isUsable, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'issuedAt')  DateTime issuedAt, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'customerDisplayName')  String customerDisplayName, @JsonKey(name: 'reason')  String reason, @JsonKey(name: 'voidReason')  String? voidReason)?  $default,) {final _that = this;
switch (_that) {
case _CreditNotePrintDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.status,_that.isUsable,_that.originalAmount,_that.availableBalance,_that.issuedAt,_that.expiresAt,_that.saleId,_that.invoiceNumber,_that.saleReturnId,_that.returnNumber,_that.customerDisplayName,_that.reason,_that.voidReason);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreditNotePrintDto implements CreditNotePrintDto {
  const _CreditNotePrintDto({@JsonKey(name: 'creditNoteId') required this.creditNoteId, @JsonKey(name: 'code') required this.code, @JsonKey(name: 'status') required this.status, @JsonKey(name: 'isUsable') required this.isUsable, @JsonKey(name: 'originalAmount') required this.originalAmount, @JsonKey(name: 'availableBalance') required this.availableBalance, @JsonKey(name: 'issuedAt') required this.issuedAt, @JsonKey(name: 'expiresAt') this.expiresAt, @JsonKey(name: 'saleId') required this.saleId, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'saleReturnId') required this.saleReturnId, @JsonKey(name: 'returnNumber') required this.returnNumber, @JsonKey(name: 'customerDisplayName') required this.customerDisplayName, @JsonKey(name: 'reason') required this.reason, @JsonKey(name: 'voidReason') this.voidReason});
  factory _CreditNotePrintDto.fromJson(Map<String, dynamic> json) => _$CreditNotePrintDtoFromJson(json);

@override@JsonKey(name: 'creditNoteId') final  String creditNoteId;
@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'status') final  String status;
@override@JsonKey(name: 'isUsable') final  bool isUsable;
@override@JsonKey(name: 'originalAmount') final  double originalAmount;
@override@JsonKey(name: 'availableBalance') final  double availableBalance;
@override@JsonKey(name: 'issuedAt') final  DateTime issuedAt;
@override@JsonKey(name: 'expiresAt') final  DateTime? expiresAt;
@override@JsonKey(name: 'saleId') final  String saleId;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'saleReturnId') final  String saleReturnId;
@override@JsonKey(name: 'returnNumber') final  String returnNumber;
@override@JsonKey(name: 'customerDisplayName') final  String customerDisplayName;
@override@JsonKey(name: 'reason') final  String reason;
@override@JsonKey(name: 'voidReason') final  String? voidReason;

/// Create a copy of CreditNotePrintDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreditNotePrintDtoCopyWith<_CreditNotePrintDto> get copyWith => __$CreditNotePrintDtoCopyWithImpl<_CreditNotePrintDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreditNotePrintDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreditNotePrintDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.status, status) || other.status == status)&&(identical(other.isUsable, isUsable) || other.isUsable == isUsable)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.issuedAt, issuedAt) || other.issuedAt == issuedAt)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.customerDisplayName, customerDisplayName) || other.customerDisplayName == customerDisplayName)&&(identical(other.reason, reason) || other.reason == reason)&&(identical(other.voidReason, voidReason) || other.voidReason == voidReason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,status,isUsable,originalAmount,availableBalance,issuedAt,expiresAt,saleId,invoiceNumber,saleReturnId,returnNumber,customerDisplayName,reason,voidReason);

@override
String toString() {
  return 'CreditNotePrintDto(creditNoteId: $creditNoteId, code: $code, status: $status, isUsable: $isUsable, originalAmount: $originalAmount, availableBalance: $availableBalance, issuedAt: $issuedAt, expiresAt: $expiresAt, saleId: $saleId, invoiceNumber: $invoiceNumber, saleReturnId: $saleReturnId, returnNumber: $returnNumber, customerDisplayName: $customerDisplayName, reason: $reason, voidReason: $voidReason)';
}


}

/// @nodoc
abstract mixin class _$CreditNotePrintDtoCopyWith<$Res> implements $CreditNotePrintDtoCopyWith<$Res> {
  factory _$CreditNotePrintDtoCopyWith(_CreditNotePrintDto value, $Res Function(_CreditNotePrintDto) _then) = __$CreditNotePrintDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'status') String status,@JsonKey(name: 'isUsable') bool isUsable,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'issuedAt') DateTime issuedAt,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'customerDisplayName') String customerDisplayName,@JsonKey(name: 'reason') String reason,@JsonKey(name: 'voidReason') String? voidReason
});




}
/// @nodoc
class __$CreditNotePrintDtoCopyWithImpl<$Res>
    implements _$CreditNotePrintDtoCopyWith<$Res> {
  __$CreditNotePrintDtoCopyWithImpl(this._self, this._then);

  final _CreditNotePrintDto _self;
  final $Res Function(_CreditNotePrintDto) _then;

/// Create a copy of CreditNotePrintDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? creditNoteId = null,Object? code = null,Object? status = null,Object? isUsable = null,Object? originalAmount = null,Object? availableBalance = null,Object? issuedAt = null,Object? expiresAt = freezed,Object? saleId = null,Object? invoiceNumber = null,Object? saleReturnId = null,Object? returnNumber = null,Object? customerDisplayName = null,Object? reason = null,Object? voidReason = freezed,}) {
  return _then(_CreditNotePrintDto(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,status: null == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String,isUsable: null == isUsable ? _self.isUsable : isUsable // ignore: cast_nullable_to_non_nullable
as bool,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,issuedAt: null == issuedAt ? _self.issuedAt : issuedAt // ignore: cast_nullable_to_non_nullable
as DateTime,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,customerDisplayName: null == customerDisplayName ? _self.customerDisplayName : customerDisplayName // ignore: cast_nullable_to_non_nullable
as String,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,voidReason: freezed == voidReason ? _self.voidReason : voidReason // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}


/// @nodoc
mixin _$CreditNotesResponseDto {

@JsonKey(name: 'items') List<CreditNoteListItemDto> get items;@JsonKey(name: 'totalCount') int get totalCount;@JsonKey(name: 'pageNumber') int get pageNumber;@JsonKey(name: 'pageSize') int get pageSize;
/// Create a copy of CreditNotesResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreditNotesResponseDtoCopyWith<CreditNotesResponseDto> get copyWith => _$CreditNotesResponseDtoCopyWithImpl<CreditNotesResponseDto>(this as CreditNotesResponseDto, _$identity);

  /// Serializes this CreditNotesResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreditNotesResponseDto&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'CreditNotesResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class $CreditNotesResponseDtoCopyWith<$Res>  {
  factory $CreditNotesResponseDtoCopyWith(CreditNotesResponseDto value, $Res Function(CreditNotesResponseDto) _then) = _$CreditNotesResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'items') List<CreditNoteListItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize
});




}
/// @nodoc
class _$CreditNotesResponseDtoCopyWithImpl<$Res>
    implements $CreditNotesResponseDtoCopyWith<$Res> {
  _$CreditNotesResponseDtoCopyWithImpl(this._self, this._then);

  final CreditNotesResponseDto _self;
  final $Res Function(CreditNotesResponseDto) _then;

/// Create a copy of CreditNotesResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<CreditNoteListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}

}


/// Adds pattern-matching-related methods to [CreditNotesResponseDto].
extension CreditNotesResponseDtoPatterns on CreditNotesResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreditNotesResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreditNotesResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreditNotesResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _CreditNotesResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreditNotesResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreditNotesResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<CreditNoteListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreditNotesResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<CreditNoteListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)  $default,) {final _that = this;
switch (_that) {
case _CreditNotesResponseDto():
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'items')  List<CreditNoteListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)?  $default,) {final _that = this;
switch (_that) {
case _CreditNotesResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreditNotesResponseDto implements CreditNotesResponseDto {
  const _CreditNotesResponseDto({@JsonKey(name: 'items') final  List<CreditNoteListItemDto> items = const [], @JsonKey(name: 'totalCount') required this.totalCount, @JsonKey(name: 'pageNumber') required this.pageNumber, @JsonKey(name: 'pageSize') required this.pageSize}): _items = items;
  factory _CreditNotesResponseDto.fromJson(Map<String, dynamic> json) => _$CreditNotesResponseDtoFromJson(json);

 final  List<CreditNoteListItemDto> _items;
@override@JsonKey(name: 'items') List<CreditNoteListItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override@JsonKey(name: 'totalCount') final  int totalCount;
@override@JsonKey(name: 'pageNumber') final  int pageNumber;
@override@JsonKey(name: 'pageSize') final  int pageSize;

/// Create a copy of CreditNotesResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreditNotesResponseDtoCopyWith<_CreditNotesResponseDto> get copyWith => __$CreditNotesResponseDtoCopyWithImpl<_CreditNotesResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreditNotesResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreditNotesResponseDto&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'CreditNotesResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class _$CreditNotesResponseDtoCopyWith<$Res> implements $CreditNotesResponseDtoCopyWith<$Res> {
  factory _$CreditNotesResponseDtoCopyWith(_CreditNotesResponseDto value, $Res Function(_CreditNotesResponseDto) _then) = __$CreditNotesResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'items') List<CreditNoteListItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize
});




}
/// @nodoc
class __$CreditNotesResponseDtoCopyWithImpl<$Res>
    implements _$CreditNotesResponseDtoCopyWith<$Res> {
  __$CreditNotesResponseDtoCopyWithImpl(this._self, this._then);

  final _CreditNotesResponseDto _self;
  final $Res Function(_CreditNotesResponseDto) _then;

/// Create a copy of CreditNotesResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_CreditNotesResponseDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<CreditNoteListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}


}

// dart format on
