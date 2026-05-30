// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'bank_account_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$BankAccountDto {

@JsonKey(name: 'id') String get id;@JsonKey(name: 'bankName') String get bankName;@JsonKey(name: 'accountNumber') String get accountNumber;@JsonKey(name: 'accountType') String? get accountType;@JsonKey(name: 'ifscCode') String? get ifscCode;@JsonKey(name: 'accountHolderName') String? get accountHolderName;
/// Create a copy of BankAccountDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$BankAccountDtoCopyWith<BankAccountDto> get copyWith => _$BankAccountDtoCopyWithImpl<BankAccountDto>(this as BankAccountDto, _$identity);

  /// Serializes this BankAccountDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is BankAccountDto&&(identical(other.id, id) || other.id == id)&&(identical(other.bankName, bankName) || other.bankName == bankName)&&(identical(other.accountNumber, accountNumber) || other.accountNumber == accountNumber)&&(identical(other.accountType, accountType) || other.accountType == accountType)&&(identical(other.ifscCode, ifscCode) || other.ifscCode == ifscCode)&&(identical(other.accountHolderName, accountHolderName) || other.accountHolderName == accountHolderName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,bankName,accountNumber,accountType,ifscCode,accountHolderName);

@override
String toString() {
  return 'BankAccountDto(id: $id, bankName: $bankName, accountNumber: $accountNumber, accountType: $accountType, ifscCode: $ifscCode, accountHolderName: $accountHolderName)';
}


}

/// @nodoc
abstract mixin class $BankAccountDtoCopyWith<$Res>  {
  factory $BankAccountDtoCopyWith(BankAccountDto value, $Res Function(BankAccountDto) _then) = _$BankAccountDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'bankName') String bankName,@JsonKey(name: 'accountNumber') String accountNumber,@JsonKey(name: 'accountType') String? accountType,@JsonKey(name: 'ifscCode') String? ifscCode,@JsonKey(name: 'accountHolderName') String? accountHolderName
});




}
/// @nodoc
class _$BankAccountDtoCopyWithImpl<$Res>
    implements $BankAccountDtoCopyWith<$Res> {
  _$BankAccountDtoCopyWithImpl(this._self, this._then);

  final BankAccountDto _self;
  final $Res Function(BankAccountDto) _then;

/// Create a copy of BankAccountDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? bankName = null,Object? accountNumber = null,Object? accountType = freezed,Object? ifscCode = freezed,Object? accountHolderName = freezed,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,bankName: null == bankName ? _self.bankName : bankName // ignore: cast_nullable_to_non_nullable
as String,accountNumber: null == accountNumber ? _self.accountNumber : accountNumber // ignore: cast_nullable_to_non_nullable
as String,accountType: freezed == accountType ? _self.accountType : accountType // ignore: cast_nullable_to_non_nullable
as String?,ifscCode: freezed == ifscCode ? _self.ifscCode : ifscCode // ignore: cast_nullable_to_non_nullable
as String?,accountHolderName: freezed == accountHolderName ? _self.accountHolderName : accountHolderName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [BankAccountDto].
extension BankAccountDtoPatterns on BankAccountDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _BankAccountDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _BankAccountDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _BankAccountDto value)  $default,){
final _that = this;
switch (_that) {
case _BankAccountDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _BankAccountDto value)?  $default,){
final _that = this;
switch (_that) {
case _BankAccountDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'bankName')  String bankName, @JsonKey(name: 'accountNumber')  String accountNumber, @JsonKey(name: 'accountType')  String? accountType, @JsonKey(name: 'ifscCode')  String? ifscCode, @JsonKey(name: 'accountHolderName')  String? accountHolderName)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _BankAccountDto() when $default != null:
return $default(_that.id,_that.bankName,_that.accountNumber,_that.accountType,_that.ifscCode,_that.accountHolderName);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'bankName')  String bankName, @JsonKey(name: 'accountNumber')  String accountNumber, @JsonKey(name: 'accountType')  String? accountType, @JsonKey(name: 'ifscCode')  String? ifscCode, @JsonKey(name: 'accountHolderName')  String? accountHolderName)  $default,) {final _that = this;
switch (_that) {
case _BankAccountDto():
return $default(_that.id,_that.bankName,_that.accountNumber,_that.accountType,_that.ifscCode,_that.accountHolderName);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'bankName')  String bankName, @JsonKey(name: 'accountNumber')  String accountNumber, @JsonKey(name: 'accountType')  String? accountType, @JsonKey(name: 'ifscCode')  String? ifscCode, @JsonKey(name: 'accountHolderName')  String? accountHolderName)?  $default,) {final _that = this;
switch (_that) {
case _BankAccountDto() when $default != null:
return $default(_that.id,_that.bankName,_that.accountNumber,_that.accountType,_that.ifscCode,_that.accountHolderName);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _BankAccountDto implements BankAccountDto {
  const _BankAccountDto({@JsonKey(name: 'id') required this.id, @JsonKey(name: 'bankName') required this.bankName, @JsonKey(name: 'accountNumber') required this.accountNumber, @JsonKey(name: 'accountType') this.accountType, @JsonKey(name: 'ifscCode') this.ifscCode, @JsonKey(name: 'accountHolderName') this.accountHolderName});
  factory _BankAccountDto.fromJson(Map<String, dynamic> json) => _$BankAccountDtoFromJson(json);

@override@JsonKey(name: 'id') final  String id;
@override@JsonKey(name: 'bankName') final  String bankName;
@override@JsonKey(name: 'accountNumber') final  String accountNumber;
@override@JsonKey(name: 'accountType') final  String? accountType;
@override@JsonKey(name: 'ifscCode') final  String? ifscCode;
@override@JsonKey(name: 'accountHolderName') final  String? accountHolderName;

/// Create a copy of BankAccountDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$BankAccountDtoCopyWith<_BankAccountDto> get copyWith => __$BankAccountDtoCopyWithImpl<_BankAccountDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$BankAccountDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _BankAccountDto&&(identical(other.id, id) || other.id == id)&&(identical(other.bankName, bankName) || other.bankName == bankName)&&(identical(other.accountNumber, accountNumber) || other.accountNumber == accountNumber)&&(identical(other.accountType, accountType) || other.accountType == accountType)&&(identical(other.ifscCode, ifscCode) || other.ifscCode == ifscCode)&&(identical(other.accountHolderName, accountHolderName) || other.accountHolderName == accountHolderName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,bankName,accountNumber,accountType,ifscCode,accountHolderName);

@override
String toString() {
  return 'BankAccountDto(id: $id, bankName: $bankName, accountNumber: $accountNumber, accountType: $accountType, ifscCode: $ifscCode, accountHolderName: $accountHolderName)';
}


}

/// @nodoc
abstract mixin class _$BankAccountDtoCopyWith<$Res> implements $BankAccountDtoCopyWith<$Res> {
  factory _$BankAccountDtoCopyWith(_BankAccountDto value, $Res Function(_BankAccountDto) _then) = __$BankAccountDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'bankName') String bankName,@JsonKey(name: 'accountNumber') String accountNumber,@JsonKey(name: 'accountType') String? accountType,@JsonKey(name: 'ifscCode') String? ifscCode,@JsonKey(name: 'accountHolderName') String? accountHolderName
});




}
/// @nodoc
class __$BankAccountDtoCopyWithImpl<$Res>
    implements _$BankAccountDtoCopyWith<$Res> {
  __$BankAccountDtoCopyWithImpl(this._self, this._then);

  final _BankAccountDto _self;
  final $Res Function(_BankAccountDto) _then;

/// Create a copy of BankAccountDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? bankName = null,Object? accountNumber = null,Object? accountType = freezed,Object? ifscCode = freezed,Object? accountHolderName = freezed,}) {
  return _then(_BankAccountDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,bankName: null == bankName ? _self.bankName : bankName // ignore: cast_nullable_to_non_nullable
as String,accountNumber: null == accountNumber ? _self.accountNumber : accountNumber // ignore: cast_nullable_to_non_nullable
as String,accountType: freezed == accountType ? _self.accountType : accountType // ignore: cast_nullable_to_non_nullable
as String?,ifscCode: freezed == ifscCode ? _self.ifscCode : ifscCode // ignore: cast_nullable_to_non_nullable
as String?,accountHolderName: freezed == accountHolderName ? _self.accountHolderName : accountHolderName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
