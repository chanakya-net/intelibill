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
mixin _$SaleDetailCreditNoteRedemptionDto {

@JsonKey(name: 'creditNoteId') String get creditNoteId;@JsonKey(name: 'code') String get code;@JsonKey(name: 'appliedAmount') double get appliedAmount;
/// Create a copy of SaleDetailCreditNoteRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailCreditNoteRedemptionDtoCopyWith<SaleDetailCreditNoteRedemptionDto> get copyWith => _$SaleDetailCreditNoteRedemptionDtoCopyWithImpl<SaleDetailCreditNoteRedemptionDto>(this as SaleDetailCreditNoteRedemptionDto, _$identity);

  /// Serializes this SaleDetailCreditNoteRedemptionDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailCreditNoteRedemptionDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.appliedAmount, appliedAmount) || other.appliedAmount == appliedAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,appliedAmount);

@override
String toString() {
  return 'SaleDetailCreditNoteRedemptionDto(creditNoteId: $creditNoteId, code: $code, appliedAmount: $appliedAmount)';
}


}

/// @nodoc
abstract mixin class $SaleDetailCreditNoteRedemptionDtoCopyWith<$Res>  {
  factory $SaleDetailCreditNoteRedemptionDtoCopyWith(SaleDetailCreditNoteRedemptionDto value, $Res Function(SaleDetailCreditNoteRedemptionDto) _then) = _$SaleDetailCreditNoteRedemptionDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'appliedAmount') double appliedAmount
});




}
/// @nodoc
class _$SaleDetailCreditNoteRedemptionDtoCopyWithImpl<$Res>
    implements $SaleDetailCreditNoteRedemptionDtoCopyWith<$Res> {
  _$SaleDetailCreditNoteRedemptionDtoCopyWithImpl(this._self, this._then);

  final SaleDetailCreditNoteRedemptionDto _self;
  final $Res Function(SaleDetailCreditNoteRedemptionDto) _then;

/// Create a copy of SaleDetailCreditNoteRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? creditNoteId = null,Object? code = null,Object? appliedAmount = null,}) {
  return _then(_self.copyWith(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,appliedAmount: null == appliedAmount ? _self.appliedAmount : appliedAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailCreditNoteRedemptionDto].
extension SaleDetailCreditNoteRedemptionDtoPatterns on SaleDetailCreditNoteRedemptionDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailCreditNoteRedemptionDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailCreditNoteRedemptionDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailCreditNoteRedemptionDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailCreditNoteRedemptionDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailCreditNoteRedemptionDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailCreditNoteRedemptionDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'appliedAmount')  double appliedAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailCreditNoteRedemptionDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.appliedAmount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'appliedAmount')  double appliedAmount)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailCreditNoteRedemptionDto():
return $default(_that.creditNoteId,_that.code,_that.appliedAmount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'appliedAmount')  double appliedAmount)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailCreditNoteRedemptionDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.appliedAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailCreditNoteRedemptionDto implements SaleDetailCreditNoteRedemptionDto {
  const _SaleDetailCreditNoteRedemptionDto({@JsonKey(name: 'creditNoteId') required this.creditNoteId, @JsonKey(name: 'code') required this.code, @JsonKey(name: 'appliedAmount') required this.appliedAmount});
  factory _SaleDetailCreditNoteRedemptionDto.fromJson(Map<String, dynamic> json) => _$SaleDetailCreditNoteRedemptionDtoFromJson(json);

@override@JsonKey(name: 'creditNoteId') final  String creditNoteId;
@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'appliedAmount') final  double appliedAmount;

/// Create a copy of SaleDetailCreditNoteRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailCreditNoteRedemptionDtoCopyWith<_SaleDetailCreditNoteRedemptionDto> get copyWith => __$SaleDetailCreditNoteRedemptionDtoCopyWithImpl<_SaleDetailCreditNoteRedemptionDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailCreditNoteRedemptionDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailCreditNoteRedemptionDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.appliedAmount, appliedAmount) || other.appliedAmount == appliedAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,appliedAmount);

@override
String toString() {
  return 'SaleDetailCreditNoteRedemptionDto(creditNoteId: $creditNoteId, code: $code, appliedAmount: $appliedAmount)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailCreditNoteRedemptionDtoCopyWith<$Res> implements $SaleDetailCreditNoteRedemptionDtoCopyWith<$Res> {
  factory _$SaleDetailCreditNoteRedemptionDtoCopyWith(_SaleDetailCreditNoteRedemptionDto value, $Res Function(_SaleDetailCreditNoteRedemptionDto) _then) = __$SaleDetailCreditNoteRedemptionDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'appliedAmount') double appliedAmount
});




}
/// @nodoc
class __$SaleDetailCreditNoteRedemptionDtoCopyWithImpl<$Res>
    implements _$SaleDetailCreditNoteRedemptionDtoCopyWith<$Res> {
  __$SaleDetailCreditNoteRedemptionDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailCreditNoteRedemptionDto _self;
  final $Res Function(_SaleDetailCreditNoteRedemptionDto) _then;

/// Create a copy of SaleDetailCreditNoteRedemptionDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? creditNoteId = null,Object? code = null,Object? appliedAmount = null,}) {
  return _then(_SaleDetailCreditNoteRedemptionDto(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,appliedAmount: null == appliedAmount ? _self.appliedAmount : appliedAmount // ignore: cast_nullable_to_non_nullable
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
mixin _$SaleDetailReturnCreditNoteDto {

@JsonKey(name: 'creditNoteId') String get creditNoteId;@JsonKey(name: 'code') String get code;@JsonKey(name: 'originalAmount') double get originalAmount;@JsonKey(name: 'availableBalance') double get availableBalance;@JsonKey(name: 'expiresAt') DateTime? get expiresAt;@JsonKey(name: 'reason') String get reason;
/// Create a copy of SaleDetailReturnCreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailReturnCreditNoteDtoCopyWith<SaleDetailReturnCreditNoteDto> get copyWith => _$SaleDetailReturnCreditNoteDtoCopyWithImpl<SaleDetailReturnCreditNoteDto>(this as SaleDetailReturnCreditNoteDto, _$identity);

  /// Serializes this SaleDetailReturnCreditNoteDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailReturnCreditNoteDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,originalAmount,availableBalance,expiresAt,reason);

@override
String toString() {
  return 'SaleDetailReturnCreditNoteDto(creditNoteId: $creditNoteId, code: $code, originalAmount: $originalAmount, availableBalance: $availableBalance, expiresAt: $expiresAt, reason: $reason)';
}


}

/// @nodoc
abstract mixin class $SaleDetailReturnCreditNoteDtoCopyWith<$Res>  {
  factory $SaleDetailReturnCreditNoteDtoCopyWith(SaleDetailReturnCreditNoteDto value, $Res Function(SaleDetailReturnCreditNoteDto) _then) = _$SaleDetailReturnCreditNoteDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'reason') String reason
});




}
/// @nodoc
class _$SaleDetailReturnCreditNoteDtoCopyWithImpl<$Res>
    implements $SaleDetailReturnCreditNoteDtoCopyWith<$Res> {
  _$SaleDetailReturnCreditNoteDtoCopyWithImpl(this._self, this._then);

  final SaleDetailReturnCreditNoteDto _self;
  final $Res Function(SaleDetailReturnCreditNoteDto) _then;

/// Create a copy of SaleDetailReturnCreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? creditNoteId = null,Object? code = null,Object? originalAmount = null,Object? availableBalance = null,Object? expiresAt = freezed,Object? reason = null,}) {
  return _then(_self.copyWith(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [SaleDetailReturnCreditNoteDto].
extension SaleDetailReturnCreditNoteDtoPatterns on SaleDetailReturnCreditNoteDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SaleDetailReturnCreditNoteDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SaleDetailReturnCreditNoteDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SaleDetailReturnCreditNoteDto value)  $default,){
final _that = this;
switch (_that) {
case _SaleDetailReturnCreditNoteDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SaleDetailReturnCreditNoteDto value)?  $default,){
final _that = this;
switch (_that) {
case _SaleDetailReturnCreditNoteDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'reason')  String reason)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailReturnCreditNoteDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.reason);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'reason')  String reason)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnCreditNoteDto():
return $default(_that.creditNoteId,_that.code,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.reason);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'creditNoteId')  String creditNoteId, @JsonKey(name: 'code')  String code, @JsonKey(name: 'originalAmount')  double originalAmount, @JsonKey(name: 'availableBalance')  double availableBalance, @JsonKey(name: 'expiresAt')  DateTime? expiresAt, @JsonKey(name: 'reason')  String reason)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnCreditNoteDto() when $default != null:
return $default(_that.creditNoteId,_that.code,_that.originalAmount,_that.availableBalance,_that.expiresAt,_that.reason);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailReturnCreditNoteDto implements SaleDetailReturnCreditNoteDto {
  const _SaleDetailReturnCreditNoteDto({@JsonKey(name: 'creditNoteId') required this.creditNoteId, @JsonKey(name: 'code') required this.code, @JsonKey(name: 'originalAmount') required this.originalAmount, @JsonKey(name: 'availableBalance') required this.availableBalance, @JsonKey(name: 'expiresAt') this.expiresAt, @JsonKey(name: 'reason') required this.reason});
  factory _SaleDetailReturnCreditNoteDto.fromJson(Map<String, dynamic> json) => _$SaleDetailReturnCreditNoteDtoFromJson(json);

@override@JsonKey(name: 'creditNoteId') final  String creditNoteId;
@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'originalAmount') final  double originalAmount;
@override@JsonKey(name: 'availableBalance') final  double availableBalance;
@override@JsonKey(name: 'expiresAt') final  DateTime? expiresAt;
@override@JsonKey(name: 'reason') final  String reason;

/// Create a copy of SaleDetailReturnCreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SaleDetailReturnCreditNoteDtoCopyWith<_SaleDetailReturnCreditNoteDto> get copyWith => __$SaleDetailReturnCreditNoteDtoCopyWithImpl<_SaleDetailReturnCreditNoteDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SaleDetailReturnCreditNoteDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailReturnCreditNoteDto&&(identical(other.creditNoteId, creditNoteId) || other.creditNoteId == creditNoteId)&&(identical(other.code, code) || other.code == code)&&(identical(other.originalAmount, originalAmount) || other.originalAmount == originalAmount)&&(identical(other.availableBalance, availableBalance) || other.availableBalance == availableBalance)&&(identical(other.expiresAt, expiresAt) || other.expiresAt == expiresAt)&&(identical(other.reason, reason) || other.reason == reason));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,creditNoteId,code,originalAmount,availableBalance,expiresAt,reason);

@override
String toString() {
  return 'SaleDetailReturnCreditNoteDto(creditNoteId: $creditNoteId, code: $code, originalAmount: $originalAmount, availableBalance: $availableBalance, expiresAt: $expiresAt, reason: $reason)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailReturnCreditNoteDtoCopyWith<$Res> implements $SaleDetailReturnCreditNoteDtoCopyWith<$Res> {
  factory _$SaleDetailReturnCreditNoteDtoCopyWith(_SaleDetailReturnCreditNoteDto value, $Res Function(_SaleDetailReturnCreditNoteDto) _then) = __$SaleDetailReturnCreditNoteDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'creditNoteId') String creditNoteId,@JsonKey(name: 'code') String code,@JsonKey(name: 'originalAmount') double originalAmount,@JsonKey(name: 'availableBalance') double availableBalance,@JsonKey(name: 'expiresAt') DateTime? expiresAt,@JsonKey(name: 'reason') String reason
});




}
/// @nodoc
class __$SaleDetailReturnCreditNoteDtoCopyWithImpl<$Res>
    implements _$SaleDetailReturnCreditNoteDtoCopyWith<$Res> {
  __$SaleDetailReturnCreditNoteDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailReturnCreditNoteDto _self;
  final $Res Function(_SaleDetailReturnCreditNoteDto) _then;

/// Create a copy of SaleDetailReturnCreditNoteDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? creditNoteId = null,Object? code = null,Object? originalAmount = null,Object? availableBalance = null,Object? expiresAt = freezed,Object? reason = null,}) {
  return _then(_SaleDetailReturnCreditNoteDto(
creditNoteId: null == creditNoteId ? _self.creditNoteId : creditNoteId // ignore: cast_nullable_to_non_nullable
as String,code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,originalAmount: null == originalAmount ? _self.originalAmount : originalAmount // ignore: cast_nullable_to_non_nullable
as double,availableBalance: null == availableBalance ? _self.availableBalance : availableBalance // ignore: cast_nullable_to_non_nullable
as double,expiresAt: freezed == expiresAt ? _self.expiresAt : expiresAt // ignore: cast_nullable_to_non_nullable
as DateTime?,reason: null == reason ? _self.reason : reason // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}


/// @nodoc
mixin _$SaleDetailReturnItemDto {

@JsonKey(name: 'saleReturnItemId') String get saleReturnItemId;@JsonKey(name: 'saleItemId') String get saleItemId;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'condition') String? get condition;@JsonKey(name: 'approvedRefundAmount') double get approvedRefundAmount;@JsonKey(name: 'taxableAmount') double get taxableAmount;@JsonKey(name: 'taxAmount') double get taxAmount;@JsonKey(name: 'notes') String? get notes;
/// Create a copy of SaleDetailReturnItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailReturnItemDtoCopyWith<SaleDetailReturnItemDto> get copyWith => _$SaleDetailReturnItemDtoCopyWithImpl<SaleDetailReturnItemDto>(this as SaleDetailReturnItemDto, _$identity);

  /// Serializes this SaleDetailReturnItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailReturnItemDto&&(identical(other.saleReturnItemId, saleReturnItemId) || other.saleReturnItemId == saleReturnItemId)&&(identical(other.saleItemId, saleItemId) || other.saleItemId == saleItemId)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.condition, condition) || other.condition == condition)&&(identical(other.approvedRefundAmount, approvedRefundAmount) || other.approvedRefundAmount == approvedRefundAmount)&&(identical(other.taxableAmount, taxableAmount) || other.taxableAmount == taxableAmount)&&(identical(other.taxAmount, taxAmount) || other.taxAmount == taxAmount)&&(identical(other.notes, notes) || other.notes == notes));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleReturnItemId,saleItemId,quantity,condition,approvedRefundAmount,taxableAmount,taxAmount,notes);

@override
String toString() {
  return 'SaleDetailReturnItemDto(saleReturnItemId: $saleReturnItemId, saleItemId: $saleItemId, quantity: $quantity, condition: $condition, approvedRefundAmount: $approvedRefundAmount, taxableAmount: $taxableAmount, taxAmount: $taxAmount, notes: $notes)';
}


}

/// @nodoc
abstract mixin class $SaleDetailReturnItemDtoCopyWith<$Res>  {
  factory $SaleDetailReturnItemDtoCopyWith(SaleDetailReturnItemDto value, $Res Function(SaleDetailReturnItemDto) _then) = _$SaleDetailReturnItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleReturnItemId') String saleReturnItemId,@JsonKey(name: 'saleItemId') String saleItemId,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'condition') String? condition,@JsonKey(name: 'approvedRefundAmount') double approvedRefundAmount,@JsonKey(name: 'taxableAmount') double taxableAmount,@JsonKey(name: 'taxAmount') double taxAmount,@JsonKey(name: 'notes') String? notes
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
@pragma('vm:prefer-inline') @override $Res call({Object? saleReturnItemId = null,Object? saleItemId = null,Object? quantity = null,Object? condition = freezed,Object? approvedRefundAmount = null,Object? taxableAmount = null,Object? taxAmount = null,Object? notes = freezed,}) {
  return _then(_self.copyWith(
saleReturnItemId: null == saleReturnItemId ? _self.saleReturnItemId : saleReturnItemId // ignore: cast_nullable_to_non_nullable
as String,saleItemId: null == saleItemId ? _self.saleItemId : saleItemId // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,condition: freezed == condition ? _self.condition : condition // ignore: cast_nullable_to_non_nullable
as String?,approvedRefundAmount: null == approvedRefundAmount ? _self.approvedRefundAmount : approvedRefundAmount // ignore: cast_nullable_to_non_nullable
as double,taxableAmount: null == taxableAmount ? _self.taxableAmount : taxableAmount // ignore: cast_nullable_to_non_nullable
as double,taxAmount: null == taxAmount ? _self.taxAmount : taxAmount // ignore: cast_nullable_to_non_nullable
as double,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleReturnItemId')  String saleReturnItemId, @JsonKey(name: 'saleItemId')  String saleItemId, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'condition')  String? condition, @JsonKey(name: 'approvedRefundAmount')  double approvedRefundAmount, @JsonKey(name: 'taxableAmount')  double taxableAmount, @JsonKey(name: 'taxAmount')  double taxAmount, @JsonKey(name: 'notes')  String? notes)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto() when $default != null:
return $default(_that.saleReturnItemId,_that.saleItemId,_that.quantity,_that.condition,_that.approvedRefundAmount,_that.taxableAmount,_that.taxAmount,_that.notes);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleReturnItemId')  String saleReturnItemId, @JsonKey(name: 'saleItemId')  String saleItemId, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'condition')  String? condition, @JsonKey(name: 'approvedRefundAmount')  double approvedRefundAmount, @JsonKey(name: 'taxableAmount')  double taxableAmount, @JsonKey(name: 'taxAmount')  double taxAmount, @JsonKey(name: 'notes')  String? notes)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto():
return $default(_that.saleReturnItemId,_that.saleItemId,_that.quantity,_that.condition,_that.approvedRefundAmount,_that.taxableAmount,_that.taxAmount,_that.notes);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleReturnItemId')  String saleReturnItemId, @JsonKey(name: 'saleItemId')  String saleItemId, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'condition')  String? condition, @JsonKey(name: 'approvedRefundAmount')  double approvedRefundAmount, @JsonKey(name: 'taxableAmount')  double taxableAmount, @JsonKey(name: 'taxAmount')  double taxAmount, @JsonKey(name: 'notes')  String? notes)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnItemDto() when $default != null:
return $default(_that.saleReturnItemId,_that.saleItemId,_that.quantity,_that.condition,_that.approvedRefundAmount,_that.taxableAmount,_that.taxAmount,_that.notes);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailReturnItemDto implements SaleDetailReturnItemDto {
  const _SaleDetailReturnItemDto({@JsonKey(name: 'saleReturnItemId') required this.saleReturnItemId, @JsonKey(name: 'saleItemId') required this.saleItemId, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'condition') this.condition, @JsonKey(name: 'approvedRefundAmount') required this.approvedRefundAmount, @JsonKey(name: 'taxableAmount') required this.taxableAmount, @JsonKey(name: 'taxAmount') required this.taxAmount, @JsonKey(name: 'notes') this.notes});
  factory _SaleDetailReturnItemDto.fromJson(Map<String, dynamic> json) => _$SaleDetailReturnItemDtoFromJson(json);

@override@JsonKey(name: 'saleReturnItemId') final  String saleReturnItemId;
@override@JsonKey(name: 'saleItemId') final  String saleItemId;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'condition') final  String? condition;
@override@JsonKey(name: 'approvedRefundAmount') final  double approvedRefundAmount;
@override@JsonKey(name: 'taxableAmount') final  double taxableAmount;
@override@JsonKey(name: 'taxAmount') final  double taxAmount;
@override@JsonKey(name: 'notes') final  String? notes;

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
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailReturnItemDto&&(identical(other.saleReturnItemId, saleReturnItemId) || other.saleReturnItemId == saleReturnItemId)&&(identical(other.saleItemId, saleItemId) || other.saleItemId == saleItemId)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.condition, condition) || other.condition == condition)&&(identical(other.approvedRefundAmount, approvedRefundAmount) || other.approvedRefundAmount == approvedRefundAmount)&&(identical(other.taxableAmount, taxableAmount) || other.taxableAmount == taxableAmount)&&(identical(other.taxAmount, taxAmount) || other.taxAmount == taxAmount)&&(identical(other.notes, notes) || other.notes == notes));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleReturnItemId,saleItemId,quantity,condition,approvedRefundAmount,taxableAmount,taxAmount,notes);

@override
String toString() {
  return 'SaleDetailReturnItemDto(saleReturnItemId: $saleReturnItemId, saleItemId: $saleItemId, quantity: $quantity, condition: $condition, approvedRefundAmount: $approvedRefundAmount, taxableAmount: $taxableAmount, taxAmount: $taxAmount, notes: $notes)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailReturnItemDtoCopyWith<$Res> implements $SaleDetailReturnItemDtoCopyWith<$Res> {
  factory _$SaleDetailReturnItemDtoCopyWith(_SaleDetailReturnItemDto value, $Res Function(_SaleDetailReturnItemDto) _then) = __$SaleDetailReturnItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleReturnItemId') String saleReturnItemId,@JsonKey(name: 'saleItemId') String saleItemId,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'condition') String? condition,@JsonKey(name: 'approvedRefundAmount') double approvedRefundAmount,@JsonKey(name: 'taxableAmount') double taxableAmount,@JsonKey(name: 'taxAmount') double taxAmount,@JsonKey(name: 'notes') String? notes
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
@override @pragma('vm:prefer-inline') $Res call({Object? saleReturnItemId = null,Object? saleItemId = null,Object? quantity = null,Object? condition = freezed,Object? approvedRefundAmount = null,Object? taxableAmount = null,Object? taxAmount = null,Object? notes = freezed,}) {
  return _then(_SaleDetailReturnItemDto(
saleReturnItemId: null == saleReturnItemId ? _self.saleReturnItemId : saleReturnItemId // ignore: cast_nullable_to_non_nullable
as String,saleItemId: null == saleItemId ? _self.saleItemId : saleItemId // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,condition: freezed == condition ? _self.condition : condition // ignore: cast_nullable_to_non_nullable
as String?,approvedRefundAmount: null == approvedRefundAmount ? _self.approvedRefundAmount : approvedRefundAmount // ignore: cast_nullable_to_non_nullable
as double,taxableAmount: null == taxableAmount ? _self.taxableAmount : taxableAmount // ignore: cast_nullable_to_non_nullable
as double,taxAmount: null == taxAmount ? _self.taxAmount : taxAmount // ignore: cast_nullable_to_non_nullable
as double,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}


/// @nodoc
mixin _$SaleDetailReturnDto {

@JsonKey(name: 'saleReturnId') String get saleReturnId;@JsonKey(name: 'returnNumber') String get returnNumber;@JsonKey(name: 'processedAt') DateTime get processedAt;@JsonKey(name: 'processedBy') String get processedBy;@JsonKey(name: 'isVoided') bool get isVoided;@JsonKey(name: 'voidedAt') DateTime? get voidedAt;@JsonKey(name: 'voidReason') String? get voidReason;@JsonKey(name: 'notes') String? get notes;@JsonKey(name: 'totalRefundAmount') double get totalRefundAmount;@JsonKey(name: 'dueReductionAmount') double get dueReductionAmount;@JsonKey(name: 'payoutAmount') double get payoutAmount;@JsonKey(name: 'payoutDestination') String? get payoutDestination;@JsonKey(name: 'totalTaxableAmount') double get totalTaxableAmount;@JsonKey(name: 'totalTaxAmount') double get totalTaxAmount;@JsonKey(name: 'creditNote') SaleDetailReturnCreditNoteDto? get creditNote;@JsonKey(name: 'items') List<SaleDetailReturnItemDto> get items;
/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailReturnDtoCopyWith<SaleDetailReturnDto> get copyWith => _$SaleDetailReturnDtoCopyWithImpl<SaleDetailReturnDto>(this as SaleDetailReturnDto, _$identity);

  /// Serializes this SaleDetailReturnDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailReturnDto&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.processedAt, processedAt) || other.processedAt == processedAt)&&(identical(other.processedBy, processedBy) || other.processedBy == processedBy)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.voidedAt, voidedAt) || other.voidedAt == voidedAt)&&(identical(other.voidReason, voidReason) || other.voidReason == voidReason)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.totalRefundAmount, totalRefundAmount) || other.totalRefundAmount == totalRefundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount)&&(identical(other.payoutAmount, payoutAmount) || other.payoutAmount == payoutAmount)&&(identical(other.payoutDestination, payoutDestination) || other.payoutDestination == payoutDestination)&&(identical(other.totalTaxableAmount, totalTaxableAmount) || other.totalTaxableAmount == totalTaxableAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.creditNote, creditNote) || other.creditNote == creditNote)&&const DeepCollectionEquality().equals(other.items, items));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleReturnId,returnNumber,processedAt,processedBy,isVoided,voidedAt,voidReason,notes,totalRefundAmount,dueReductionAmount,payoutAmount,payoutDestination,totalTaxableAmount,totalTaxAmount,creditNote,const DeepCollectionEquality().hash(items));

@override
String toString() {
  return 'SaleDetailReturnDto(saleReturnId: $saleReturnId, returnNumber: $returnNumber, processedAt: $processedAt, processedBy: $processedBy, isVoided: $isVoided, voidedAt: $voidedAt, voidReason: $voidReason, notes: $notes, totalRefundAmount: $totalRefundAmount, dueReductionAmount: $dueReductionAmount, payoutAmount: $payoutAmount, payoutDestination: $payoutDestination, totalTaxableAmount: $totalTaxableAmount, totalTaxAmount: $totalTaxAmount, creditNote: $creditNote, items: $items)';
}


}

/// @nodoc
abstract mixin class $SaleDetailReturnDtoCopyWith<$Res>  {
  factory $SaleDetailReturnDtoCopyWith(SaleDetailReturnDto value, $Res Function(SaleDetailReturnDto) _then) = _$SaleDetailReturnDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'processedAt') DateTime processedAt,@JsonKey(name: 'processedBy') String processedBy,@JsonKey(name: 'isVoided') bool isVoided,@JsonKey(name: 'voidedAt') DateTime? voidedAt,@JsonKey(name: 'voidReason') String? voidReason,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'totalRefundAmount') double totalRefundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount,@JsonKey(name: 'payoutAmount') double payoutAmount,@JsonKey(name: 'payoutDestination') String? payoutDestination,@JsonKey(name: 'totalTaxableAmount') double totalTaxableAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'creditNote') SaleDetailReturnCreditNoteDto? creditNote,@JsonKey(name: 'items') List<SaleDetailReturnItemDto> items
});


$SaleDetailReturnCreditNoteDtoCopyWith<$Res>? get creditNote;

}
/// @nodoc
class _$SaleDetailReturnDtoCopyWithImpl<$Res>
    implements $SaleDetailReturnDtoCopyWith<$Res> {
  _$SaleDetailReturnDtoCopyWithImpl(this._self, this._then);

  final SaleDetailReturnDto _self;
  final $Res Function(SaleDetailReturnDto) _then;

/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? saleReturnId = null,Object? returnNumber = null,Object? processedAt = null,Object? processedBy = null,Object? isVoided = null,Object? voidedAt = freezed,Object? voidReason = freezed,Object? notes = freezed,Object? totalRefundAmount = null,Object? dueReductionAmount = null,Object? payoutAmount = null,Object? payoutDestination = freezed,Object? totalTaxableAmount = null,Object? totalTaxAmount = null,Object? creditNote = freezed,Object? items = null,}) {
  return _then(_self.copyWith(
saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,processedAt: null == processedAt ? _self.processedAt : processedAt // ignore: cast_nullable_to_non_nullable
as DateTime,processedBy: null == processedBy ? _self.processedBy : processedBy // ignore: cast_nullable_to_non_nullable
as String,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,voidedAt: freezed == voidedAt ? _self.voidedAt : voidedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,voidReason: freezed == voidReason ? _self.voidReason : voidReason // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,totalRefundAmount: null == totalRefundAmount ? _self.totalRefundAmount : totalRefundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,payoutAmount: null == payoutAmount ? _self.payoutAmount : payoutAmount // ignore: cast_nullable_to_non_nullable
as double,payoutDestination: freezed == payoutDestination ? _self.payoutDestination : payoutDestination // ignore: cast_nullable_to_non_nullable
as String?,totalTaxableAmount: null == totalTaxableAmount ? _self.totalTaxableAmount : totalTaxableAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,creditNote: freezed == creditNote ? _self.creditNote : creditNote // ignore: cast_nullable_to_non_nullable
as SaleDetailReturnCreditNoteDto?,items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnItemDto>,
  ));
}
/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SaleDetailReturnCreditNoteDtoCopyWith<$Res>? get creditNote {
    if (_self.creditNote == null) {
    return null;
  }

  return $SaleDetailReturnCreditNoteDtoCopyWith<$Res>(_self.creditNote!, (value) {
    return _then(_self.copyWith(creditNote: value));
  });
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'processedAt')  DateTime processedAt, @JsonKey(name: 'processedBy')  String processedBy, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'voidedAt')  DateTime? voidedAt, @JsonKey(name: 'voidReason')  String? voidReason, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'totalRefundAmount')  double totalRefundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount, @JsonKey(name: 'payoutAmount')  double payoutAmount, @JsonKey(name: 'payoutDestination')  String? payoutDestination, @JsonKey(name: 'totalTaxableAmount')  double totalTaxableAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNote')  SaleDetailReturnCreditNoteDto? creditNote, @JsonKey(name: 'items')  List<SaleDetailReturnItemDto> items)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailReturnDto() when $default != null:
return $default(_that.saleReturnId,_that.returnNumber,_that.processedAt,_that.processedBy,_that.isVoided,_that.voidedAt,_that.voidReason,_that.notes,_that.totalRefundAmount,_that.dueReductionAmount,_that.payoutAmount,_that.payoutDestination,_that.totalTaxableAmount,_that.totalTaxAmount,_that.creditNote,_that.items);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'processedAt')  DateTime processedAt, @JsonKey(name: 'processedBy')  String processedBy, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'voidedAt')  DateTime? voidedAt, @JsonKey(name: 'voidReason')  String? voidReason, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'totalRefundAmount')  double totalRefundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount, @JsonKey(name: 'payoutAmount')  double payoutAmount, @JsonKey(name: 'payoutDestination')  String? payoutDestination, @JsonKey(name: 'totalTaxableAmount')  double totalTaxableAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNote')  SaleDetailReturnCreditNoteDto? creditNote, @JsonKey(name: 'items')  List<SaleDetailReturnItemDto> items)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnDto():
return $default(_that.saleReturnId,_that.returnNumber,_that.processedAt,_that.processedBy,_that.isVoided,_that.voidedAt,_that.voidReason,_that.notes,_that.totalRefundAmount,_that.dueReductionAmount,_that.payoutAmount,_that.payoutDestination,_that.totalTaxableAmount,_that.totalTaxAmount,_that.creditNote,_that.items);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleReturnId')  String saleReturnId, @JsonKey(name: 'returnNumber')  String returnNumber, @JsonKey(name: 'processedAt')  DateTime processedAt, @JsonKey(name: 'processedBy')  String processedBy, @JsonKey(name: 'isVoided')  bool isVoided, @JsonKey(name: 'voidedAt')  DateTime? voidedAt, @JsonKey(name: 'voidReason')  String? voidReason, @JsonKey(name: 'notes')  String? notes, @JsonKey(name: 'totalRefundAmount')  double totalRefundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount, @JsonKey(name: 'payoutAmount')  double payoutAmount, @JsonKey(name: 'payoutDestination')  String? payoutDestination, @JsonKey(name: 'totalTaxableAmount')  double totalTaxableAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNote')  SaleDetailReturnCreditNoteDto? creditNote, @JsonKey(name: 'items')  List<SaleDetailReturnItemDto> items)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailReturnDto() when $default != null:
return $default(_that.saleReturnId,_that.returnNumber,_that.processedAt,_that.processedBy,_that.isVoided,_that.voidedAt,_that.voidReason,_that.notes,_that.totalRefundAmount,_that.dueReductionAmount,_that.payoutAmount,_that.payoutDestination,_that.totalTaxableAmount,_that.totalTaxAmount,_that.creditNote,_that.items);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailReturnDto implements SaleDetailReturnDto {
  const _SaleDetailReturnDto({@JsonKey(name: 'saleReturnId') required this.saleReturnId, @JsonKey(name: 'returnNumber') required this.returnNumber, @JsonKey(name: 'processedAt') required this.processedAt, @JsonKey(name: 'processedBy') required this.processedBy, @JsonKey(name: 'isVoided') this.isVoided = false, @JsonKey(name: 'voidedAt') this.voidedAt, @JsonKey(name: 'voidReason') this.voidReason, @JsonKey(name: 'notes') this.notes, @JsonKey(name: 'totalRefundAmount') required this.totalRefundAmount, @JsonKey(name: 'dueReductionAmount') required this.dueReductionAmount, @JsonKey(name: 'payoutAmount') required this.payoutAmount, @JsonKey(name: 'payoutDestination') this.payoutDestination, @JsonKey(name: 'totalTaxableAmount') required this.totalTaxableAmount, @JsonKey(name: 'totalTaxAmount') required this.totalTaxAmount, @JsonKey(name: 'creditNote') this.creditNote, @JsonKey(name: 'items') final  List<SaleDetailReturnItemDto> items = const []}): _items = items;
  factory _SaleDetailReturnDto.fromJson(Map<String, dynamic> json) => _$SaleDetailReturnDtoFromJson(json);

@override@JsonKey(name: 'saleReturnId') final  String saleReturnId;
@override@JsonKey(name: 'returnNumber') final  String returnNumber;
@override@JsonKey(name: 'processedAt') final  DateTime processedAt;
@override@JsonKey(name: 'processedBy') final  String processedBy;
@override@JsonKey(name: 'isVoided') final  bool isVoided;
@override@JsonKey(name: 'voidedAt') final  DateTime? voidedAt;
@override@JsonKey(name: 'voidReason') final  String? voidReason;
@override@JsonKey(name: 'notes') final  String? notes;
@override@JsonKey(name: 'totalRefundAmount') final  double totalRefundAmount;
@override@JsonKey(name: 'dueReductionAmount') final  double dueReductionAmount;
@override@JsonKey(name: 'payoutAmount') final  double payoutAmount;
@override@JsonKey(name: 'payoutDestination') final  String? payoutDestination;
@override@JsonKey(name: 'totalTaxableAmount') final  double totalTaxableAmount;
@override@JsonKey(name: 'totalTaxAmount') final  double totalTaxAmount;
@override@JsonKey(name: 'creditNote') final  SaleDetailReturnCreditNoteDto? creditNote;
 final  List<SaleDetailReturnItemDto> _items;
@override@JsonKey(name: 'items') List<SaleDetailReturnItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}


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
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailReturnDto&&(identical(other.saleReturnId, saleReturnId) || other.saleReturnId == saleReturnId)&&(identical(other.returnNumber, returnNumber) || other.returnNumber == returnNumber)&&(identical(other.processedAt, processedAt) || other.processedAt == processedAt)&&(identical(other.processedBy, processedBy) || other.processedBy == processedBy)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.voidedAt, voidedAt) || other.voidedAt == voidedAt)&&(identical(other.voidReason, voidReason) || other.voidReason == voidReason)&&(identical(other.notes, notes) || other.notes == notes)&&(identical(other.totalRefundAmount, totalRefundAmount) || other.totalRefundAmount == totalRefundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount)&&(identical(other.payoutAmount, payoutAmount) || other.payoutAmount == payoutAmount)&&(identical(other.payoutDestination, payoutDestination) || other.payoutDestination == payoutDestination)&&(identical(other.totalTaxableAmount, totalTaxableAmount) || other.totalTaxableAmount == totalTaxableAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.creditNote, creditNote) || other.creditNote == creditNote)&&const DeepCollectionEquality().equals(other._items, _items));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,saleReturnId,returnNumber,processedAt,processedBy,isVoided,voidedAt,voidReason,notes,totalRefundAmount,dueReductionAmount,payoutAmount,payoutDestination,totalTaxableAmount,totalTaxAmount,creditNote,const DeepCollectionEquality().hash(_items));

@override
String toString() {
  return 'SaleDetailReturnDto(saleReturnId: $saleReturnId, returnNumber: $returnNumber, processedAt: $processedAt, processedBy: $processedBy, isVoided: $isVoided, voidedAt: $voidedAt, voidReason: $voidReason, notes: $notes, totalRefundAmount: $totalRefundAmount, dueReductionAmount: $dueReductionAmount, payoutAmount: $payoutAmount, payoutDestination: $payoutDestination, totalTaxableAmount: $totalTaxableAmount, totalTaxAmount: $totalTaxAmount, creditNote: $creditNote, items: $items)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailReturnDtoCopyWith<$Res> implements $SaleDetailReturnDtoCopyWith<$Res> {
  factory _$SaleDetailReturnDtoCopyWith(_SaleDetailReturnDto value, $Res Function(_SaleDetailReturnDto) _then) = __$SaleDetailReturnDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleReturnId') String saleReturnId,@JsonKey(name: 'returnNumber') String returnNumber,@JsonKey(name: 'processedAt') DateTime processedAt,@JsonKey(name: 'processedBy') String processedBy,@JsonKey(name: 'isVoided') bool isVoided,@JsonKey(name: 'voidedAt') DateTime? voidedAt,@JsonKey(name: 'voidReason') String? voidReason,@JsonKey(name: 'notes') String? notes,@JsonKey(name: 'totalRefundAmount') double totalRefundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount,@JsonKey(name: 'payoutAmount') double payoutAmount,@JsonKey(name: 'payoutDestination') String? payoutDestination,@JsonKey(name: 'totalTaxableAmount') double totalTaxableAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'creditNote') SaleDetailReturnCreditNoteDto? creditNote,@JsonKey(name: 'items') List<SaleDetailReturnItemDto> items
});


@override $SaleDetailReturnCreditNoteDtoCopyWith<$Res>? get creditNote;

}
/// @nodoc
class __$SaleDetailReturnDtoCopyWithImpl<$Res>
    implements _$SaleDetailReturnDtoCopyWith<$Res> {
  __$SaleDetailReturnDtoCopyWithImpl(this._self, this._then);

  final _SaleDetailReturnDto _self;
  final $Res Function(_SaleDetailReturnDto) _then;

/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? saleReturnId = null,Object? returnNumber = null,Object? processedAt = null,Object? processedBy = null,Object? isVoided = null,Object? voidedAt = freezed,Object? voidReason = freezed,Object? notes = freezed,Object? totalRefundAmount = null,Object? dueReductionAmount = null,Object? payoutAmount = null,Object? payoutDestination = freezed,Object? totalTaxableAmount = null,Object? totalTaxAmount = null,Object? creditNote = freezed,Object? items = null,}) {
  return _then(_SaleDetailReturnDto(
saleReturnId: null == saleReturnId ? _self.saleReturnId : saleReturnId // ignore: cast_nullable_to_non_nullable
as String,returnNumber: null == returnNumber ? _self.returnNumber : returnNumber // ignore: cast_nullable_to_non_nullable
as String,processedAt: null == processedAt ? _self.processedAt : processedAt // ignore: cast_nullable_to_non_nullable
as DateTime,processedBy: null == processedBy ? _self.processedBy : processedBy // ignore: cast_nullable_to_non_nullable
as String,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,voidedAt: freezed == voidedAt ? _self.voidedAt : voidedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,voidReason: freezed == voidReason ? _self.voidReason : voidReason // ignore: cast_nullable_to_non_nullable
as String?,notes: freezed == notes ? _self.notes : notes // ignore: cast_nullable_to_non_nullable
as String?,totalRefundAmount: null == totalRefundAmount ? _self.totalRefundAmount : totalRefundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,payoutAmount: null == payoutAmount ? _self.payoutAmount : payoutAmount // ignore: cast_nullable_to_non_nullable
as double,payoutDestination: freezed == payoutDestination ? _self.payoutDestination : payoutDestination // ignore: cast_nullable_to_non_nullable
as String?,totalTaxableAmount: null == totalTaxableAmount ? _self.totalTaxableAmount : totalTaxableAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,creditNote: freezed == creditNote ? _self.creditNote : creditNote // ignore: cast_nullable_to_non_nullable
as SaleDetailReturnCreditNoteDto?,items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnItemDto>,
  ));
}

/// Create a copy of SaleDetailReturnDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SaleDetailReturnCreditNoteDtoCopyWith<$Res>? get creditNote {
    if (_self.creditNote == null) {
    return null;
  }

  return $SaleDetailReturnCreditNoteDtoCopyWith<$Res>(_self.creditNote!, (value) {
    return _then(_self.copyWith(creditNote: value));
  });
}
}


/// @nodoc
mixin _$SaleDetailItemDto {

@JsonKey(name: 'saleItemId') String get saleItemId;@JsonKey(name: 'lineType') String get lineType;@JsonKey(name: 'itemId') String? get itemId;@JsonKey(name: 'inventoryBatchId') String? get inventoryBatchId;@JsonKey(name: 'serviceId') String? get serviceId;@JsonKey(name: 'lineCode') String get lineCode;@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'quantity') double get quantity;@JsonKey(name: 'salesPrice') double get salesPrice;@JsonKey(name: 'originalSalesPrice') double get originalSalesPrice;@JsonKey(name: 'finalSalesPrice') double get finalSalesPrice;@JsonKey(name: 'preTaxAmountBeforeDiscount') double get preTaxAmountBeforeDiscount;@JsonKey(name: 'itemDiscountAmount') double get itemDiscountAmount;@JsonKey(name: 'saleDiscountAmount') double get saleDiscountAmount;@JsonKey(name: 'taxableAmount') double get taxableAmount;@JsonKey(name: 'taxAmount') double get taxAmount;@JsonKey(name: 'totalAmount') double get totalAmount;@JsonKey(name: 'savingsAmount') double get savingsAmount;@JsonKey(name: 'taxRatePercent') double get taxRatePercent;@JsonKey(name: 'isPriceIncludingTax') bool get isPriceIncludingTax;@JsonKey(name: 'hasPriceMismatch') bool get hasPriceMismatch;@JsonKey(name: 'hsnCode') String? get hsnCode;@JsonKey(name: 'returnedQuantity') double get returnedQuantity;@JsonKey(name: 'returnableQuantity') double get returnableQuantity;@JsonKey(name: 'returnStatus') String get returnStatus;
/// Create a copy of SaleDetailItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailItemDtoCopyWith<SaleDetailItemDto> get copyWith => _$SaleDetailItemDtoCopyWithImpl<SaleDetailItemDto>(this as SaleDetailItemDto, _$identity);

  /// Serializes this SaleDetailItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailItemDto&&(identical(other.saleItemId, saleItemId) || other.saleItemId == saleItemId)&&(identical(other.lineType, lineType) || other.lineType == lineType)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.serviceId, serviceId) || other.serviceId == serviceId)&&(identical(other.lineCode, lineCode) || other.lineCode == lineCode)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.originalSalesPrice, originalSalesPrice) || other.originalSalesPrice == originalSalesPrice)&&(identical(other.finalSalesPrice, finalSalesPrice) || other.finalSalesPrice == finalSalesPrice)&&(identical(other.preTaxAmountBeforeDiscount, preTaxAmountBeforeDiscount) || other.preTaxAmountBeforeDiscount == preTaxAmountBeforeDiscount)&&(identical(other.itemDiscountAmount, itemDiscountAmount) || other.itemDiscountAmount == itemDiscountAmount)&&(identical(other.saleDiscountAmount, saleDiscountAmount) || other.saleDiscountAmount == saleDiscountAmount)&&(identical(other.taxableAmount, taxableAmount) || other.taxableAmount == taxableAmount)&&(identical(other.taxAmount, taxAmount) || other.taxAmount == taxAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.savingsAmount, savingsAmount) || other.savingsAmount == savingsAmount)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.isPriceIncludingTax, isPriceIncludingTax) || other.isPriceIncludingTax == isPriceIncludingTax)&&(identical(other.hasPriceMismatch, hasPriceMismatch) || other.hasPriceMismatch == hasPriceMismatch)&&(identical(other.hsnCode, hsnCode) || other.hsnCode == hsnCode)&&(identical(other.returnedQuantity, returnedQuantity) || other.returnedQuantity == returnedQuantity)&&(identical(other.returnableQuantity, returnableQuantity) || other.returnableQuantity == returnableQuantity)&&(identical(other.returnStatus, returnStatus) || other.returnStatus == returnStatus));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,saleItemId,lineType,itemId,inventoryBatchId,serviceId,lineCode,itemName,quantity,salesPrice,originalSalesPrice,finalSalesPrice,preTaxAmountBeforeDiscount,itemDiscountAmount,saleDiscountAmount,taxableAmount,taxAmount,totalAmount,savingsAmount,taxRatePercent,isPriceIncludingTax,hasPriceMismatch,hsnCode,returnedQuantity,returnableQuantity,returnStatus]);

@override
String toString() {
  return 'SaleDetailItemDto(saleItemId: $saleItemId, lineType: $lineType, itemId: $itemId, inventoryBatchId: $inventoryBatchId, serviceId: $serviceId, lineCode: $lineCode, itemName: $itemName, quantity: $quantity, salesPrice: $salesPrice, originalSalesPrice: $originalSalesPrice, finalSalesPrice: $finalSalesPrice, preTaxAmountBeforeDiscount: $preTaxAmountBeforeDiscount, itemDiscountAmount: $itemDiscountAmount, saleDiscountAmount: $saleDiscountAmount, taxableAmount: $taxableAmount, taxAmount: $taxAmount, totalAmount: $totalAmount, savingsAmount: $savingsAmount, taxRatePercent: $taxRatePercent, isPriceIncludingTax: $isPriceIncludingTax, hasPriceMismatch: $hasPriceMismatch, hsnCode: $hsnCode, returnedQuantity: $returnedQuantity, returnableQuantity: $returnableQuantity, returnStatus: $returnStatus)';
}


}

/// @nodoc
abstract mixin class $SaleDetailItemDtoCopyWith<$Res>  {
  factory $SaleDetailItemDtoCopyWith(SaleDetailItemDto value, $Res Function(SaleDetailItemDto) _then) = _$SaleDetailItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleItemId') String saleItemId,@JsonKey(name: 'lineType') String lineType,@JsonKey(name: 'itemId') String? itemId,@JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,@JsonKey(name: 'serviceId') String? serviceId,@JsonKey(name: 'lineCode') String lineCode,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'originalSalesPrice') double originalSalesPrice,@JsonKey(name: 'finalSalesPrice') double finalSalesPrice,@JsonKey(name: 'preTaxAmountBeforeDiscount') double preTaxAmountBeforeDiscount,@JsonKey(name: 'itemDiscountAmount') double itemDiscountAmount,@JsonKey(name: 'saleDiscountAmount') double saleDiscountAmount,@JsonKey(name: 'taxableAmount') double taxableAmount,@JsonKey(name: 'taxAmount') double taxAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'savingsAmount') double savingsAmount,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'isPriceIncludingTax') bool isPriceIncludingTax,@JsonKey(name: 'hasPriceMismatch') bool hasPriceMismatch,@JsonKey(name: 'hsnCode') String? hsnCode,@JsonKey(name: 'returnedQuantity') double returnedQuantity,@JsonKey(name: 'returnableQuantity') double returnableQuantity,@JsonKey(name: 'returnStatus') String returnStatus
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
@pragma('vm:prefer-inline') @override $Res call({Object? saleItemId = null,Object? lineType = null,Object? itemId = freezed,Object? inventoryBatchId = freezed,Object? serviceId = freezed,Object? lineCode = null,Object? itemName = null,Object? quantity = null,Object? salesPrice = null,Object? originalSalesPrice = null,Object? finalSalesPrice = null,Object? preTaxAmountBeforeDiscount = null,Object? itemDiscountAmount = null,Object? saleDiscountAmount = null,Object? taxableAmount = null,Object? taxAmount = null,Object? totalAmount = null,Object? savingsAmount = null,Object? taxRatePercent = null,Object? isPriceIncludingTax = null,Object? hasPriceMismatch = null,Object? hsnCode = freezed,Object? returnedQuantity = null,Object? returnableQuantity = null,Object? returnStatus = null,}) {
  return _then(_self.copyWith(
saleItemId: null == saleItemId ? _self.saleItemId : saleItemId // ignore: cast_nullable_to_non_nullable
as String,lineType: null == lineType ? _self.lineType : lineType // ignore: cast_nullable_to_non_nullable
as String,itemId: freezed == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String?,inventoryBatchId: freezed == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String?,serviceId: freezed == serviceId ? _self.serviceId : serviceId // ignore: cast_nullable_to_non_nullable
as String?,lineCode: null == lineCode ? _self.lineCode : lineCode // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,originalSalesPrice: null == originalSalesPrice ? _self.originalSalesPrice : originalSalesPrice // ignore: cast_nullable_to_non_nullable
as double,finalSalesPrice: null == finalSalesPrice ? _self.finalSalesPrice : finalSalesPrice // ignore: cast_nullable_to_non_nullable
as double,preTaxAmountBeforeDiscount: null == preTaxAmountBeforeDiscount ? _self.preTaxAmountBeforeDiscount : preTaxAmountBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,itemDiscountAmount: null == itemDiscountAmount ? _self.itemDiscountAmount : itemDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,saleDiscountAmount: null == saleDiscountAmount ? _self.saleDiscountAmount : saleDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,taxableAmount: null == taxableAmount ? _self.taxableAmount : taxableAmount // ignore: cast_nullable_to_non_nullable
as double,taxAmount: null == taxAmount ? _self.taxAmount : taxAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,savingsAmount: null == savingsAmount ? _self.savingsAmount : savingsAmount // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,isPriceIncludingTax: null == isPriceIncludingTax ? _self.isPriceIncludingTax : isPriceIncludingTax // ignore: cast_nullable_to_non_nullable
as bool,hasPriceMismatch: null == hasPriceMismatch ? _self.hasPriceMismatch : hasPriceMismatch // ignore: cast_nullable_to_non_nullable
as bool,hsnCode: freezed == hsnCode ? _self.hsnCode : hsnCode // ignore: cast_nullable_to_non_nullable
as String?,returnedQuantity: null == returnedQuantity ? _self.returnedQuantity : returnedQuantity // ignore: cast_nullable_to_non_nullable
as double,returnableQuantity: null == returnableQuantity ? _self.returnableQuantity : returnableQuantity // ignore: cast_nullable_to_non_nullable
as double,returnStatus: null == returnStatus ? _self.returnStatus : returnStatus // ignore: cast_nullable_to_non_nullable
as String,
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleItemId')  String saleItemId, @JsonKey(name: 'lineType')  String lineType, @JsonKey(name: 'itemId')  String? itemId, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'serviceId')  String? serviceId, @JsonKey(name: 'lineCode')  String lineCode, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'originalSalesPrice')  double originalSalesPrice, @JsonKey(name: 'finalSalesPrice')  double finalSalesPrice, @JsonKey(name: 'preTaxAmountBeforeDiscount')  double preTaxAmountBeforeDiscount, @JsonKey(name: 'itemDiscountAmount')  double itemDiscountAmount, @JsonKey(name: 'saleDiscountAmount')  double saleDiscountAmount, @JsonKey(name: 'taxableAmount')  double taxableAmount, @JsonKey(name: 'taxAmount')  double taxAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'savingsAmount')  double savingsAmount, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'isPriceIncludingTax')  bool isPriceIncludingTax, @JsonKey(name: 'hasPriceMismatch')  bool hasPriceMismatch, @JsonKey(name: 'hsnCode')  String? hsnCode, @JsonKey(name: 'returnedQuantity')  double returnedQuantity, @JsonKey(name: 'returnableQuantity')  double returnableQuantity, @JsonKey(name: 'returnStatus')  String returnStatus)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailItemDto() when $default != null:
return $default(_that.saleItemId,_that.lineType,_that.itemId,_that.inventoryBatchId,_that.serviceId,_that.lineCode,_that.itemName,_that.quantity,_that.salesPrice,_that.originalSalesPrice,_that.finalSalesPrice,_that.preTaxAmountBeforeDiscount,_that.itemDiscountAmount,_that.saleDiscountAmount,_that.taxableAmount,_that.taxAmount,_that.totalAmount,_that.savingsAmount,_that.taxRatePercent,_that.isPriceIncludingTax,_that.hasPriceMismatch,_that.hsnCode,_that.returnedQuantity,_that.returnableQuantity,_that.returnStatus);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleItemId')  String saleItemId, @JsonKey(name: 'lineType')  String lineType, @JsonKey(name: 'itemId')  String? itemId, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'serviceId')  String? serviceId, @JsonKey(name: 'lineCode')  String lineCode, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'originalSalesPrice')  double originalSalesPrice, @JsonKey(name: 'finalSalesPrice')  double finalSalesPrice, @JsonKey(name: 'preTaxAmountBeforeDiscount')  double preTaxAmountBeforeDiscount, @JsonKey(name: 'itemDiscountAmount')  double itemDiscountAmount, @JsonKey(name: 'saleDiscountAmount')  double saleDiscountAmount, @JsonKey(name: 'taxableAmount')  double taxableAmount, @JsonKey(name: 'taxAmount')  double taxAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'savingsAmount')  double savingsAmount, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'isPriceIncludingTax')  bool isPriceIncludingTax, @JsonKey(name: 'hasPriceMismatch')  bool hasPriceMismatch, @JsonKey(name: 'hsnCode')  String? hsnCode, @JsonKey(name: 'returnedQuantity')  double returnedQuantity, @JsonKey(name: 'returnableQuantity')  double returnableQuantity, @JsonKey(name: 'returnStatus')  String returnStatus)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailItemDto():
return $default(_that.saleItemId,_that.lineType,_that.itemId,_that.inventoryBatchId,_that.serviceId,_that.lineCode,_that.itemName,_that.quantity,_that.salesPrice,_that.originalSalesPrice,_that.finalSalesPrice,_that.preTaxAmountBeforeDiscount,_that.itemDiscountAmount,_that.saleDiscountAmount,_that.taxableAmount,_that.taxAmount,_that.totalAmount,_that.savingsAmount,_that.taxRatePercent,_that.isPriceIncludingTax,_that.hasPriceMismatch,_that.hsnCode,_that.returnedQuantity,_that.returnableQuantity,_that.returnStatus);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleItemId')  String saleItemId, @JsonKey(name: 'lineType')  String lineType, @JsonKey(name: 'itemId')  String? itemId, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'serviceId')  String? serviceId, @JsonKey(name: 'lineCode')  String lineCode, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'quantity')  double quantity, @JsonKey(name: 'salesPrice')  double salesPrice, @JsonKey(name: 'originalSalesPrice')  double originalSalesPrice, @JsonKey(name: 'finalSalesPrice')  double finalSalesPrice, @JsonKey(name: 'preTaxAmountBeforeDiscount')  double preTaxAmountBeforeDiscount, @JsonKey(name: 'itemDiscountAmount')  double itemDiscountAmount, @JsonKey(name: 'saleDiscountAmount')  double saleDiscountAmount, @JsonKey(name: 'taxableAmount')  double taxableAmount, @JsonKey(name: 'taxAmount')  double taxAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'savingsAmount')  double savingsAmount, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'isPriceIncludingTax')  bool isPriceIncludingTax, @JsonKey(name: 'hasPriceMismatch')  bool hasPriceMismatch, @JsonKey(name: 'hsnCode')  String? hsnCode, @JsonKey(name: 'returnedQuantity')  double returnedQuantity, @JsonKey(name: 'returnableQuantity')  double returnableQuantity, @JsonKey(name: 'returnStatus')  String returnStatus)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailItemDto() when $default != null:
return $default(_that.saleItemId,_that.lineType,_that.itemId,_that.inventoryBatchId,_that.serviceId,_that.lineCode,_that.itemName,_that.quantity,_that.salesPrice,_that.originalSalesPrice,_that.finalSalesPrice,_that.preTaxAmountBeforeDiscount,_that.itemDiscountAmount,_that.saleDiscountAmount,_that.taxableAmount,_that.taxAmount,_that.totalAmount,_that.savingsAmount,_that.taxRatePercent,_that.isPriceIncludingTax,_that.hasPriceMismatch,_that.hsnCode,_that.returnedQuantity,_that.returnableQuantity,_that.returnStatus);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailItemDto implements SaleDetailItemDto {
  const _SaleDetailItemDto({@JsonKey(name: 'saleItemId') required this.saleItemId, @JsonKey(name: 'lineType') required this.lineType, @JsonKey(name: 'itemId') this.itemId, @JsonKey(name: 'inventoryBatchId') this.inventoryBatchId, @JsonKey(name: 'serviceId') this.serviceId, @JsonKey(name: 'lineCode') required this.lineCode, @JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'quantity') required this.quantity, @JsonKey(name: 'salesPrice') required this.salesPrice, @JsonKey(name: 'originalSalesPrice') this.originalSalesPrice = 0.0, @JsonKey(name: 'finalSalesPrice') this.finalSalesPrice = 0.0, @JsonKey(name: 'preTaxAmountBeforeDiscount') this.preTaxAmountBeforeDiscount = 0.0, @JsonKey(name: 'itemDiscountAmount') this.itemDiscountAmount = 0.0, @JsonKey(name: 'saleDiscountAmount') this.saleDiscountAmount = 0.0, @JsonKey(name: 'taxableAmount') this.taxableAmount = 0.0, @JsonKey(name: 'taxAmount') this.taxAmount = 0.0, @JsonKey(name: 'totalAmount') this.totalAmount = 0.0, @JsonKey(name: 'savingsAmount') this.savingsAmount = 0.0, @JsonKey(name: 'taxRatePercent') required this.taxRatePercent, @JsonKey(name: 'isPriceIncludingTax') required this.isPriceIncludingTax, @JsonKey(name: 'hasPriceMismatch') this.hasPriceMismatch = false, @JsonKey(name: 'hsnCode') this.hsnCode, @JsonKey(name: 'returnedQuantity') this.returnedQuantity = 0.0, @JsonKey(name: 'returnableQuantity') this.returnableQuantity = 0.0, @JsonKey(name: 'returnStatus') this.returnStatus = 'NotReturned'});
  factory _SaleDetailItemDto.fromJson(Map<String, dynamic> json) => _$SaleDetailItemDtoFromJson(json);

@override@JsonKey(name: 'saleItemId') final  String saleItemId;
@override@JsonKey(name: 'lineType') final  String lineType;
@override@JsonKey(name: 'itemId') final  String? itemId;
@override@JsonKey(name: 'inventoryBatchId') final  String? inventoryBatchId;
@override@JsonKey(name: 'serviceId') final  String? serviceId;
@override@JsonKey(name: 'lineCode') final  String lineCode;
@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'quantity') final  double quantity;
@override@JsonKey(name: 'salesPrice') final  double salesPrice;
@override@JsonKey(name: 'originalSalesPrice') final  double originalSalesPrice;
@override@JsonKey(name: 'finalSalesPrice') final  double finalSalesPrice;
@override@JsonKey(name: 'preTaxAmountBeforeDiscount') final  double preTaxAmountBeforeDiscount;
@override@JsonKey(name: 'itemDiscountAmount') final  double itemDiscountAmount;
@override@JsonKey(name: 'saleDiscountAmount') final  double saleDiscountAmount;
@override@JsonKey(name: 'taxableAmount') final  double taxableAmount;
@override@JsonKey(name: 'taxAmount') final  double taxAmount;
@override@JsonKey(name: 'totalAmount') final  double totalAmount;
@override@JsonKey(name: 'savingsAmount') final  double savingsAmount;
@override@JsonKey(name: 'taxRatePercent') final  double taxRatePercent;
@override@JsonKey(name: 'isPriceIncludingTax') final  bool isPriceIncludingTax;
@override@JsonKey(name: 'hasPriceMismatch') final  bool hasPriceMismatch;
@override@JsonKey(name: 'hsnCode') final  String? hsnCode;
@override@JsonKey(name: 'returnedQuantity') final  double returnedQuantity;
@override@JsonKey(name: 'returnableQuantity') final  double returnableQuantity;
@override@JsonKey(name: 'returnStatus') final  String returnStatus;

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
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailItemDto&&(identical(other.saleItemId, saleItemId) || other.saleItemId == saleItemId)&&(identical(other.lineType, lineType) || other.lineType == lineType)&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.serviceId, serviceId) || other.serviceId == serviceId)&&(identical(other.lineCode, lineCode) || other.lineCode == lineCode)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.quantity, quantity) || other.quantity == quantity)&&(identical(other.salesPrice, salesPrice) || other.salesPrice == salesPrice)&&(identical(other.originalSalesPrice, originalSalesPrice) || other.originalSalesPrice == originalSalesPrice)&&(identical(other.finalSalesPrice, finalSalesPrice) || other.finalSalesPrice == finalSalesPrice)&&(identical(other.preTaxAmountBeforeDiscount, preTaxAmountBeforeDiscount) || other.preTaxAmountBeforeDiscount == preTaxAmountBeforeDiscount)&&(identical(other.itemDiscountAmount, itemDiscountAmount) || other.itemDiscountAmount == itemDiscountAmount)&&(identical(other.saleDiscountAmount, saleDiscountAmount) || other.saleDiscountAmount == saleDiscountAmount)&&(identical(other.taxableAmount, taxableAmount) || other.taxableAmount == taxableAmount)&&(identical(other.taxAmount, taxAmount) || other.taxAmount == taxAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.savingsAmount, savingsAmount) || other.savingsAmount == savingsAmount)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.isPriceIncludingTax, isPriceIncludingTax) || other.isPriceIncludingTax == isPriceIncludingTax)&&(identical(other.hasPriceMismatch, hasPriceMismatch) || other.hasPriceMismatch == hasPriceMismatch)&&(identical(other.hsnCode, hsnCode) || other.hsnCode == hsnCode)&&(identical(other.returnedQuantity, returnedQuantity) || other.returnedQuantity == returnedQuantity)&&(identical(other.returnableQuantity, returnableQuantity) || other.returnableQuantity == returnableQuantity)&&(identical(other.returnStatus, returnStatus) || other.returnStatus == returnStatus));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,saleItemId,lineType,itemId,inventoryBatchId,serviceId,lineCode,itemName,quantity,salesPrice,originalSalesPrice,finalSalesPrice,preTaxAmountBeforeDiscount,itemDiscountAmount,saleDiscountAmount,taxableAmount,taxAmount,totalAmount,savingsAmount,taxRatePercent,isPriceIncludingTax,hasPriceMismatch,hsnCode,returnedQuantity,returnableQuantity,returnStatus]);

@override
String toString() {
  return 'SaleDetailItemDto(saleItemId: $saleItemId, lineType: $lineType, itemId: $itemId, inventoryBatchId: $inventoryBatchId, serviceId: $serviceId, lineCode: $lineCode, itemName: $itemName, quantity: $quantity, salesPrice: $salesPrice, originalSalesPrice: $originalSalesPrice, finalSalesPrice: $finalSalesPrice, preTaxAmountBeforeDiscount: $preTaxAmountBeforeDiscount, itemDiscountAmount: $itemDiscountAmount, saleDiscountAmount: $saleDiscountAmount, taxableAmount: $taxableAmount, taxAmount: $taxAmount, totalAmount: $totalAmount, savingsAmount: $savingsAmount, taxRatePercent: $taxRatePercent, isPriceIncludingTax: $isPriceIncludingTax, hasPriceMismatch: $hasPriceMismatch, hsnCode: $hsnCode, returnedQuantity: $returnedQuantity, returnableQuantity: $returnableQuantity, returnStatus: $returnStatus)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailItemDtoCopyWith<$Res> implements $SaleDetailItemDtoCopyWith<$Res> {
  factory _$SaleDetailItemDtoCopyWith(_SaleDetailItemDto value, $Res Function(_SaleDetailItemDto) _then) = __$SaleDetailItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleItemId') String saleItemId,@JsonKey(name: 'lineType') String lineType,@JsonKey(name: 'itemId') String? itemId,@JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,@JsonKey(name: 'serviceId') String? serviceId,@JsonKey(name: 'lineCode') String lineCode,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'quantity') double quantity,@JsonKey(name: 'salesPrice') double salesPrice,@JsonKey(name: 'originalSalesPrice') double originalSalesPrice,@JsonKey(name: 'finalSalesPrice') double finalSalesPrice,@JsonKey(name: 'preTaxAmountBeforeDiscount') double preTaxAmountBeforeDiscount,@JsonKey(name: 'itemDiscountAmount') double itemDiscountAmount,@JsonKey(name: 'saleDiscountAmount') double saleDiscountAmount,@JsonKey(name: 'taxableAmount') double taxableAmount,@JsonKey(name: 'taxAmount') double taxAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'savingsAmount') double savingsAmount,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'isPriceIncludingTax') bool isPriceIncludingTax,@JsonKey(name: 'hasPriceMismatch') bool hasPriceMismatch,@JsonKey(name: 'hsnCode') String? hsnCode,@JsonKey(name: 'returnedQuantity') double returnedQuantity,@JsonKey(name: 'returnableQuantity') double returnableQuantity,@JsonKey(name: 'returnStatus') String returnStatus
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
@override @pragma('vm:prefer-inline') $Res call({Object? saleItemId = null,Object? lineType = null,Object? itemId = freezed,Object? inventoryBatchId = freezed,Object? serviceId = freezed,Object? lineCode = null,Object? itemName = null,Object? quantity = null,Object? salesPrice = null,Object? originalSalesPrice = null,Object? finalSalesPrice = null,Object? preTaxAmountBeforeDiscount = null,Object? itemDiscountAmount = null,Object? saleDiscountAmount = null,Object? taxableAmount = null,Object? taxAmount = null,Object? totalAmount = null,Object? savingsAmount = null,Object? taxRatePercent = null,Object? isPriceIncludingTax = null,Object? hasPriceMismatch = null,Object? hsnCode = freezed,Object? returnedQuantity = null,Object? returnableQuantity = null,Object? returnStatus = null,}) {
  return _then(_SaleDetailItemDto(
saleItemId: null == saleItemId ? _self.saleItemId : saleItemId // ignore: cast_nullable_to_non_nullable
as String,lineType: null == lineType ? _self.lineType : lineType // ignore: cast_nullable_to_non_nullable
as String,itemId: freezed == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String?,inventoryBatchId: freezed == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String?,serviceId: freezed == serviceId ? _self.serviceId : serviceId // ignore: cast_nullable_to_non_nullable
as String?,lineCode: null == lineCode ? _self.lineCode : lineCode // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,quantity: null == quantity ? _self.quantity : quantity // ignore: cast_nullable_to_non_nullable
as double,salesPrice: null == salesPrice ? _self.salesPrice : salesPrice // ignore: cast_nullable_to_non_nullable
as double,originalSalesPrice: null == originalSalesPrice ? _self.originalSalesPrice : originalSalesPrice // ignore: cast_nullable_to_non_nullable
as double,finalSalesPrice: null == finalSalesPrice ? _self.finalSalesPrice : finalSalesPrice // ignore: cast_nullable_to_non_nullable
as double,preTaxAmountBeforeDiscount: null == preTaxAmountBeforeDiscount ? _self.preTaxAmountBeforeDiscount : preTaxAmountBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,itemDiscountAmount: null == itemDiscountAmount ? _self.itemDiscountAmount : itemDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,saleDiscountAmount: null == saleDiscountAmount ? _self.saleDiscountAmount : saleDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,taxableAmount: null == taxableAmount ? _self.taxableAmount : taxableAmount // ignore: cast_nullable_to_non_nullable
as double,taxAmount: null == taxAmount ? _self.taxAmount : taxAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,savingsAmount: null == savingsAmount ? _self.savingsAmount : savingsAmount // ignore: cast_nullable_to_non_nullable
as double,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,isPriceIncludingTax: null == isPriceIncludingTax ? _self.isPriceIncludingTax : isPriceIncludingTax // ignore: cast_nullable_to_non_nullable
as bool,hasPriceMismatch: null == hasPriceMismatch ? _self.hasPriceMismatch : hasPriceMismatch // ignore: cast_nullable_to_non_nullable
as bool,hsnCode: freezed == hsnCode ? _self.hsnCode : hsnCode // ignore: cast_nullable_to_non_nullable
as String?,returnedQuantity: null == returnedQuantity ? _self.returnedQuantity : returnedQuantity // ignore: cast_nullable_to_non_nullable
as double,returnableQuantity: null == returnableQuantity ? _self.returnableQuantity : returnableQuantity // ignore: cast_nullable_to_non_nullable
as double,returnStatus: null == returnStatus ? _self.returnStatus : returnStatus // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}


/// @nodoc
mixin _$SaleDetailDto {

@JsonKey(name: 'saleId') String get saleId;@JsonKey(name: 'invoiceNumber') String get invoiceNumber;@JsonKey(name: 'customerId') String? get customerId;@JsonKey(name: 'customerName') String? get customerName;@JsonKey(name: 'customerPhone') String? get customerPhone;@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int get paymentMethod;@JsonKey(name: 'soldAt') DateTime get soldAt;@JsonKey(name: 'paidAmount') double get paidAmount;@JsonKey(name: 'dueAmount') double get dueAmount;@JsonKey(name: 'totalBeforeDiscount') double get totalBeforeDiscount;@JsonKey(name: 'totalDiscountAmount') double get totalDiscountAmount;@JsonKey(name: 'totalAmount') double get totalAmount;@JsonKey(name: 'totalTaxAmount') double get totalTaxAmount;@JsonKey(name: 'creditNoteAppliedAmount') double get creditNoteAppliedAmount;@JsonKey(name: 'items') List<SaleDetailItemDto> get items;@JsonKey(name: 'warnings') List<String> get warnings;@JsonKey(name: 'returns') List<SaleDetailReturnDto> get returns;@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailCreditNoteRedemptionDto> get creditNoteRedemptions;@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> get settlements;@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> get discounts;@JsonKey(name: 'status') String? get status;@JsonKey(name: 'refundAmount') double get refundAmount;@JsonKey(name: 'dueReductionAmount') double get dueReductionAmount;
/// Create a copy of SaleDetailDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SaleDetailDtoCopyWith<SaleDetailDto> get copyWith => _$SaleDetailDtoCopyWithImpl<SaleDetailDto>(this as SaleDetailDto, _$identity);

  /// Serializes this SaleDetailDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SaleDetailDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.customerName, customerName) || other.customerName == customerName)&&(identical(other.customerPhone, customerPhone) || other.customerPhone == customerPhone)&&(identical(other.paymentMethod, paymentMethod) || other.paymentMethod == paymentMethod)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&(identical(other.paidAmount, paidAmount) || other.paidAmount == paidAmount)&&(identical(other.dueAmount, dueAmount) || other.dueAmount == dueAmount)&&(identical(other.totalBeforeDiscount, totalBeforeDiscount) || other.totalBeforeDiscount == totalBeforeDiscount)&&(identical(other.totalDiscountAmount, totalDiscountAmount) || other.totalDiscountAmount == totalDiscountAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.creditNoteAppliedAmount, creditNoteAppliedAmount) || other.creditNoteAppliedAmount == creditNoteAppliedAmount)&&const DeepCollectionEquality().equals(other.items, items)&&const DeepCollectionEquality().equals(other.warnings, warnings)&&const DeepCollectionEquality().equals(other.returns, returns)&&const DeepCollectionEquality().equals(other.creditNoteRedemptions, creditNoteRedemptions)&&const DeepCollectionEquality().equals(other.settlements, settlements)&&const DeepCollectionEquality().equals(other.discounts, discounts)&&(identical(other.status, status) || other.status == status)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,saleId,invoiceNumber,customerId,customerName,customerPhone,paymentMethod,soldAt,paidAmount,dueAmount,totalBeforeDiscount,totalDiscountAmount,totalAmount,totalTaxAmount,creditNoteAppliedAmount,const DeepCollectionEquality().hash(items),const DeepCollectionEquality().hash(warnings),const DeepCollectionEquality().hash(returns),const DeepCollectionEquality().hash(creditNoteRedemptions),const DeepCollectionEquality().hash(settlements),const DeepCollectionEquality().hash(discounts),status,refundAmount,dueReductionAmount]);

@override
String toString() {
  return 'SaleDetailDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerId: $customerId, customerName: $customerName, customerPhone: $customerPhone, paymentMethod: $paymentMethod, soldAt: $soldAt, paidAmount: $paidAmount, dueAmount: $dueAmount, totalBeforeDiscount: $totalBeforeDiscount, totalDiscountAmount: $totalDiscountAmount, totalAmount: $totalAmount, totalTaxAmount: $totalTaxAmount, creditNoteAppliedAmount: $creditNoteAppliedAmount, items: $items, warnings: $warnings, returns: $returns, creditNoteRedemptions: $creditNoteRedemptions, settlements: $settlements, discounts: $discounts, status: $status, refundAmount: $refundAmount, dueReductionAmount: $dueReductionAmount)';
}


}

/// @nodoc
abstract mixin class $SaleDetailDtoCopyWith<$Res>  {
  factory $SaleDetailDtoCopyWith(SaleDetailDto value, $Res Function(SaleDetailDto) _then) = _$SaleDetailDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerId') String? customerId,@JsonKey(name: 'customerName') String? customerName,@JsonKey(name: 'customerPhone') String? customerPhone,@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int paymentMethod,@JsonKey(name: 'soldAt') DateTime soldAt,@JsonKey(name: 'paidAmount') double paidAmount,@JsonKey(name: 'dueAmount') double dueAmount,@JsonKey(name: 'totalBeforeDiscount') double totalBeforeDiscount,@JsonKey(name: 'totalDiscountAmount') double totalDiscountAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'creditNoteAppliedAmount') double creditNoteAppliedAmount,@JsonKey(name: 'items') List<SaleDetailItemDto> items,@JsonKey(name: 'warnings') List<String> warnings,@JsonKey(name: 'returns') List<SaleDetailReturnDto> returns,@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions,@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> settlements,@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> discounts,@JsonKey(name: 'status') String? status,@JsonKey(name: 'refundAmount') double refundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount
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
@pragma('vm:prefer-inline') @override $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerId = freezed,Object? customerName = freezed,Object? customerPhone = freezed,Object? paymentMethod = null,Object? soldAt = null,Object? paidAmount = null,Object? dueAmount = null,Object? totalBeforeDiscount = null,Object? totalDiscountAmount = null,Object? totalAmount = null,Object? totalTaxAmount = null,Object? creditNoteAppliedAmount = null,Object? items = null,Object? warnings = null,Object? returns = null,Object? creditNoteRedemptions = null,Object? settlements = null,Object? discounts = null,Object? status = freezed,Object? refundAmount = null,Object? dueReductionAmount = null,}) {
  return _then(_self.copyWith(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerId: freezed == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String?,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,customerPhone: freezed == customerPhone ? _self.customerPhone : customerPhone // ignore: cast_nullable_to_non_nullable
as String?,paymentMethod: null == paymentMethod ? _self.paymentMethod : paymentMethod // ignore: cast_nullable_to_non_nullable
as int,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as DateTime,paidAmount: null == paidAmount ? _self.paidAmount : paidAmount // ignore: cast_nullable_to_non_nullable
as double,dueAmount: null == dueAmount ? _self.dueAmount : dueAmount // ignore: cast_nullable_to_non_nullable
as double,totalBeforeDiscount: null == totalBeforeDiscount ? _self.totalBeforeDiscount : totalBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,totalDiscountAmount: null == totalDiscountAmount ? _self.totalDiscountAmount : totalDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,creditNoteAppliedAmount: null == creditNoteAppliedAmount ? _self.creditNoteAppliedAmount : creditNoteAppliedAmount // ignore: cast_nullable_to_non_nullable
as double,items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailItemDto>,warnings: null == warnings ? _self.warnings : warnings // ignore: cast_nullable_to_non_nullable
as List<String>,returns: null == returns ? _self.returns : returns // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnDto>,creditNoteRedemptions: null == creditNoteRedemptions ? _self.creditNoteRedemptions : creditNoteRedemptions // ignore: cast_nullable_to_non_nullable
as List<SaleDetailCreditNoteRedemptionDto>,settlements: null == settlements ? _self.settlements : settlements // ignore: cast_nullable_to_non_nullable
as List<SaleDetailSettlementDto>,discounts: null == discounts ? _self.discounts : discounts // ignore: cast_nullable_to_non_nullable
as List<SaleDetailDiscountDto>,status: freezed == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount')  double creditNoteAppliedAmount, @JsonKey(name: 'items')  List<SaleDetailItemDto> items, @JsonKey(name: 'warnings')  List<String> warnings, @JsonKey(name: 'returns')  List<SaleDetailReturnDto> returns, @JsonKey(name: 'creditNoteRedemptions')  List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions, @JsonKey(name: 'settlements')  List<SaleDetailSettlementDto> settlements, @JsonKey(name: 'discounts')  List<SaleDetailDiscountDto> discounts, @JsonKey(name: 'status')  String? status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SaleDetailDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.customerName,_that.customerPhone,_that.paymentMethod,_that.soldAt,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.creditNoteAppliedAmount,_that.items,_that.warnings,_that.returns,_that.creditNoteRedemptions,_that.settlements,_that.discounts,_that.status,_that.refundAmount,_that.dueReductionAmount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount')  double creditNoteAppliedAmount, @JsonKey(name: 'items')  List<SaleDetailItemDto> items, @JsonKey(name: 'warnings')  List<String> warnings, @JsonKey(name: 'returns')  List<SaleDetailReturnDto> returns, @JsonKey(name: 'creditNoteRedemptions')  List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions, @JsonKey(name: 'settlements')  List<SaleDetailSettlementDto> settlements, @JsonKey(name: 'discounts')  List<SaleDetailDiscountDto> discounts, @JsonKey(name: 'status')  String? status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)  $default,) {final _that = this;
switch (_that) {
case _SaleDetailDto():
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.customerName,_that.customerPhone,_that.paymentMethod,_that.soldAt,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.creditNoteAppliedAmount,_that.items,_that.warnings,_that.returns,_that.creditNoteRedemptions,_that.settlements,_that.discounts,_that.status,_that.refundAmount,_that.dueReductionAmount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'saleId')  String saleId, @JsonKey(name: 'invoiceNumber')  String invoiceNumber, @JsonKey(name: 'customerId')  String? customerId, @JsonKey(name: 'customerName')  String? customerName, @JsonKey(name: 'customerPhone')  String? customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson)  int paymentMethod, @JsonKey(name: 'soldAt')  DateTime soldAt, @JsonKey(name: 'paidAmount')  double paidAmount, @JsonKey(name: 'dueAmount')  double dueAmount, @JsonKey(name: 'totalBeforeDiscount')  double totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount')  double totalDiscountAmount, @JsonKey(name: 'totalAmount')  double totalAmount, @JsonKey(name: 'totalTaxAmount')  double totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount')  double creditNoteAppliedAmount, @JsonKey(name: 'items')  List<SaleDetailItemDto> items, @JsonKey(name: 'warnings')  List<String> warnings, @JsonKey(name: 'returns')  List<SaleDetailReturnDto> returns, @JsonKey(name: 'creditNoteRedemptions')  List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions, @JsonKey(name: 'settlements')  List<SaleDetailSettlementDto> settlements, @JsonKey(name: 'discounts')  List<SaleDetailDiscountDto> discounts, @JsonKey(name: 'status')  String? status, @JsonKey(name: 'refundAmount')  double refundAmount, @JsonKey(name: 'dueReductionAmount')  double dueReductionAmount)?  $default,) {final _that = this;
switch (_that) {
case _SaleDetailDto() when $default != null:
return $default(_that.saleId,_that.invoiceNumber,_that.customerId,_that.customerName,_that.customerPhone,_that.paymentMethod,_that.soldAt,_that.paidAmount,_that.dueAmount,_that.totalBeforeDiscount,_that.totalDiscountAmount,_that.totalAmount,_that.totalTaxAmount,_that.creditNoteAppliedAmount,_that.items,_that.warnings,_that.returns,_that.creditNoteRedemptions,_that.settlements,_that.discounts,_that.status,_that.refundAmount,_that.dueReductionAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SaleDetailDto implements SaleDetailDto {
  const _SaleDetailDto({@JsonKey(name: 'saleId') required this.saleId, @JsonKey(name: 'invoiceNumber') required this.invoiceNumber, @JsonKey(name: 'customerId') this.customerId, @JsonKey(name: 'customerName') this.customerName, @JsonKey(name: 'customerPhone') this.customerPhone, @JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) required this.paymentMethod, @JsonKey(name: 'soldAt') required this.soldAt, @JsonKey(name: 'paidAmount') required this.paidAmount, @JsonKey(name: 'dueAmount') required this.dueAmount, @JsonKey(name: 'totalBeforeDiscount') required this.totalBeforeDiscount, @JsonKey(name: 'totalDiscountAmount') required this.totalDiscountAmount, @JsonKey(name: 'totalAmount') required this.totalAmount, @JsonKey(name: 'totalTaxAmount') required this.totalTaxAmount, @JsonKey(name: 'creditNoteAppliedAmount') this.creditNoteAppliedAmount = 0.0, @JsonKey(name: 'items') final  List<SaleDetailItemDto> items = const [], @JsonKey(name: 'warnings') final  List<String> warnings = const [], @JsonKey(name: 'returns') final  List<SaleDetailReturnDto> returns = const [], @JsonKey(name: 'creditNoteRedemptions') final  List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions = const [], @JsonKey(name: 'settlements') final  List<SaleDetailSettlementDto> settlements = const [], @JsonKey(name: 'discounts') final  List<SaleDetailDiscountDto> discounts = const [], @JsonKey(name: 'status') this.status, @JsonKey(name: 'refundAmount') this.refundAmount = 0.0, @JsonKey(name: 'dueReductionAmount') this.dueReductionAmount = 0.0}): _items = items,_warnings = warnings,_returns = returns,_creditNoteRedemptions = creditNoteRedemptions,_settlements = settlements,_discounts = discounts;
  factory _SaleDetailDto.fromJson(Map<String, dynamic> json) => _$SaleDetailDtoFromJson(json);

@override@JsonKey(name: 'saleId') final  String saleId;
@override@JsonKey(name: 'invoiceNumber') final  String invoiceNumber;
@override@JsonKey(name: 'customerId') final  String? customerId;
@override@JsonKey(name: 'customerName') final  String? customerName;
@override@JsonKey(name: 'customerPhone') final  String? customerPhone;
@override@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) final  int paymentMethod;
@override@JsonKey(name: 'soldAt') final  DateTime soldAt;
@override@JsonKey(name: 'paidAmount') final  double paidAmount;
@override@JsonKey(name: 'dueAmount') final  double dueAmount;
@override@JsonKey(name: 'totalBeforeDiscount') final  double totalBeforeDiscount;
@override@JsonKey(name: 'totalDiscountAmount') final  double totalDiscountAmount;
@override@JsonKey(name: 'totalAmount') final  double totalAmount;
@override@JsonKey(name: 'totalTaxAmount') final  double totalTaxAmount;
@override@JsonKey(name: 'creditNoteAppliedAmount') final  double creditNoteAppliedAmount;
 final  List<SaleDetailItemDto> _items;
@override@JsonKey(name: 'items') List<SaleDetailItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

 final  List<String> _warnings;
@override@JsonKey(name: 'warnings') List<String> get warnings {
  if (_warnings is EqualUnmodifiableListView) return _warnings;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_warnings);
}

 final  List<SaleDetailReturnDto> _returns;
@override@JsonKey(name: 'returns') List<SaleDetailReturnDto> get returns {
  if (_returns is EqualUnmodifiableListView) return _returns;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_returns);
}

 final  List<SaleDetailCreditNoteRedemptionDto> _creditNoteRedemptions;
@override@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailCreditNoteRedemptionDto> get creditNoteRedemptions {
  if (_creditNoteRedemptions is EqualUnmodifiableListView) return _creditNoteRedemptions;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_creditNoteRedemptions);
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
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SaleDetailDto&&(identical(other.saleId, saleId) || other.saleId == saleId)&&(identical(other.invoiceNumber, invoiceNumber) || other.invoiceNumber == invoiceNumber)&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.customerName, customerName) || other.customerName == customerName)&&(identical(other.customerPhone, customerPhone) || other.customerPhone == customerPhone)&&(identical(other.paymentMethod, paymentMethod) || other.paymentMethod == paymentMethod)&&(identical(other.soldAt, soldAt) || other.soldAt == soldAt)&&(identical(other.paidAmount, paidAmount) || other.paidAmount == paidAmount)&&(identical(other.dueAmount, dueAmount) || other.dueAmount == dueAmount)&&(identical(other.totalBeforeDiscount, totalBeforeDiscount) || other.totalBeforeDiscount == totalBeforeDiscount)&&(identical(other.totalDiscountAmount, totalDiscountAmount) || other.totalDiscountAmount == totalDiscountAmount)&&(identical(other.totalAmount, totalAmount) || other.totalAmount == totalAmount)&&(identical(other.totalTaxAmount, totalTaxAmount) || other.totalTaxAmount == totalTaxAmount)&&(identical(other.creditNoteAppliedAmount, creditNoteAppliedAmount) || other.creditNoteAppliedAmount == creditNoteAppliedAmount)&&const DeepCollectionEquality().equals(other._items, _items)&&const DeepCollectionEquality().equals(other._warnings, _warnings)&&const DeepCollectionEquality().equals(other._returns, _returns)&&const DeepCollectionEquality().equals(other._creditNoteRedemptions, _creditNoteRedemptions)&&const DeepCollectionEquality().equals(other._settlements, _settlements)&&const DeepCollectionEquality().equals(other._discounts, _discounts)&&(identical(other.status, status) || other.status == status)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount)&&(identical(other.dueReductionAmount, dueReductionAmount) || other.dueReductionAmount == dueReductionAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hashAll([runtimeType,saleId,invoiceNumber,customerId,customerName,customerPhone,paymentMethod,soldAt,paidAmount,dueAmount,totalBeforeDiscount,totalDiscountAmount,totalAmount,totalTaxAmount,creditNoteAppliedAmount,const DeepCollectionEquality().hash(_items),const DeepCollectionEquality().hash(_warnings),const DeepCollectionEquality().hash(_returns),const DeepCollectionEquality().hash(_creditNoteRedemptions),const DeepCollectionEquality().hash(_settlements),const DeepCollectionEquality().hash(_discounts),status,refundAmount,dueReductionAmount]);

@override
String toString() {
  return 'SaleDetailDto(saleId: $saleId, invoiceNumber: $invoiceNumber, customerId: $customerId, customerName: $customerName, customerPhone: $customerPhone, paymentMethod: $paymentMethod, soldAt: $soldAt, paidAmount: $paidAmount, dueAmount: $dueAmount, totalBeforeDiscount: $totalBeforeDiscount, totalDiscountAmount: $totalDiscountAmount, totalAmount: $totalAmount, totalTaxAmount: $totalTaxAmount, creditNoteAppliedAmount: $creditNoteAppliedAmount, items: $items, warnings: $warnings, returns: $returns, creditNoteRedemptions: $creditNoteRedemptions, settlements: $settlements, discounts: $discounts, status: $status, refundAmount: $refundAmount, dueReductionAmount: $dueReductionAmount)';
}


}

/// @nodoc
abstract mixin class _$SaleDetailDtoCopyWith<$Res> implements $SaleDetailDtoCopyWith<$Res> {
  factory _$SaleDetailDtoCopyWith(_SaleDetailDto value, $Res Function(_SaleDetailDto) _then) = __$SaleDetailDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'saleId') String saleId,@JsonKey(name: 'invoiceNumber') String invoiceNumber,@JsonKey(name: 'customerId') String? customerId,@JsonKey(name: 'customerName') String? customerName,@JsonKey(name: 'customerPhone') String? customerPhone,@JsonKey(name: 'paymentMethod', fromJson: paymentMethodFromJson) int paymentMethod,@JsonKey(name: 'soldAt') DateTime soldAt,@JsonKey(name: 'paidAmount') double paidAmount,@JsonKey(name: 'dueAmount') double dueAmount,@JsonKey(name: 'totalBeforeDiscount') double totalBeforeDiscount,@JsonKey(name: 'totalDiscountAmount') double totalDiscountAmount,@JsonKey(name: 'totalAmount') double totalAmount,@JsonKey(name: 'totalTaxAmount') double totalTaxAmount,@JsonKey(name: 'creditNoteAppliedAmount') double creditNoteAppliedAmount,@JsonKey(name: 'items') List<SaleDetailItemDto> items,@JsonKey(name: 'warnings') List<String> warnings,@JsonKey(name: 'returns') List<SaleDetailReturnDto> returns,@JsonKey(name: 'creditNoteRedemptions') List<SaleDetailCreditNoteRedemptionDto> creditNoteRedemptions,@JsonKey(name: 'settlements') List<SaleDetailSettlementDto> settlements,@JsonKey(name: 'discounts') List<SaleDetailDiscountDto> discounts,@JsonKey(name: 'status') String? status,@JsonKey(name: 'refundAmount') double refundAmount,@JsonKey(name: 'dueReductionAmount') double dueReductionAmount
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
@override @pragma('vm:prefer-inline') $Res call({Object? saleId = null,Object? invoiceNumber = null,Object? customerId = freezed,Object? customerName = freezed,Object? customerPhone = freezed,Object? paymentMethod = null,Object? soldAt = null,Object? paidAmount = null,Object? dueAmount = null,Object? totalBeforeDiscount = null,Object? totalDiscountAmount = null,Object? totalAmount = null,Object? totalTaxAmount = null,Object? creditNoteAppliedAmount = null,Object? items = null,Object? warnings = null,Object? returns = null,Object? creditNoteRedemptions = null,Object? settlements = null,Object? discounts = null,Object? status = freezed,Object? refundAmount = null,Object? dueReductionAmount = null,}) {
  return _then(_SaleDetailDto(
saleId: null == saleId ? _self.saleId : saleId // ignore: cast_nullable_to_non_nullable
as String,invoiceNumber: null == invoiceNumber ? _self.invoiceNumber : invoiceNumber // ignore: cast_nullable_to_non_nullable
as String,customerId: freezed == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String?,customerName: freezed == customerName ? _self.customerName : customerName // ignore: cast_nullable_to_non_nullable
as String?,customerPhone: freezed == customerPhone ? _self.customerPhone : customerPhone // ignore: cast_nullable_to_non_nullable
as String?,paymentMethod: null == paymentMethod ? _self.paymentMethod : paymentMethod // ignore: cast_nullable_to_non_nullable
as int,soldAt: null == soldAt ? _self.soldAt : soldAt // ignore: cast_nullable_to_non_nullable
as DateTime,paidAmount: null == paidAmount ? _self.paidAmount : paidAmount // ignore: cast_nullable_to_non_nullable
as double,dueAmount: null == dueAmount ? _self.dueAmount : dueAmount // ignore: cast_nullable_to_non_nullable
as double,totalBeforeDiscount: null == totalBeforeDiscount ? _self.totalBeforeDiscount : totalBeforeDiscount // ignore: cast_nullable_to_non_nullable
as double,totalDiscountAmount: null == totalDiscountAmount ? _self.totalDiscountAmount : totalDiscountAmount // ignore: cast_nullable_to_non_nullable
as double,totalAmount: null == totalAmount ? _self.totalAmount : totalAmount // ignore: cast_nullable_to_non_nullable
as double,totalTaxAmount: null == totalTaxAmount ? _self.totalTaxAmount : totalTaxAmount // ignore: cast_nullable_to_non_nullable
as double,creditNoteAppliedAmount: null == creditNoteAppliedAmount ? _self.creditNoteAppliedAmount : creditNoteAppliedAmount // ignore: cast_nullable_to_non_nullable
as double,items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<SaleDetailItemDto>,warnings: null == warnings ? _self._warnings : warnings // ignore: cast_nullable_to_non_nullable
as List<String>,returns: null == returns ? _self._returns : returns // ignore: cast_nullable_to_non_nullable
as List<SaleDetailReturnDto>,creditNoteRedemptions: null == creditNoteRedemptions ? _self._creditNoteRedemptions : creditNoteRedemptions // ignore: cast_nullable_to_non_nullable
as List<SaleDetailCreditNoteRedemptionDto>,settlements: null == settlements ? _self._settlements : settlements // ignore: cast_nullable_to_non_nullable
as List<SaleDetailSettlementDto>,discounts: null == discounts ? _self._discounts : discounts // ignore: cast_nullable_to_non_nullable
as List<SaleDetailDiscountDto>,status: freezed == status ? _self.status : status // ignore: cast_nullable_to_non_nullable
as String?,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,dueReductionAmount: null == dueReductionAmount ? _self.dueReductionAmount : dueReductionAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
