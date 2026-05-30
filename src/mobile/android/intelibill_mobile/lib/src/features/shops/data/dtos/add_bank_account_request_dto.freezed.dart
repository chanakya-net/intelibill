// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'add_bank_account_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AddBankAccountRequestDto {

@JsonKey(name: 'bankName') String get bankName;@JsonKey(name: 'accountNumber') String get accountNumber;@JsonKey(name: 'accountType') String get accountType;@JsonKey(name: 'ifscCode') String get ifscCode;@JsonKey(name: 'accountHolderName') String get accountHolderName;
/// Create a copy of AddBankAccountRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddBankAccountRequestDtoCopyWith<AddBankAccountRequestDto> get copyWith => _$AddBankAccountRequestDtoCopyWithImpl<AddBankAccountRequestDto>(this as AddBankAccountRequestDto, _$identity);

  /// Serializes this AddBankAccountRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddBankAccountRequestDto&&(identical(other.bankName, bankName) || other.bankName == bankName)&&(identical(other.accountNumber, accountNumber) || other.accountNumber == accountNumber)&&(identical(other.accountType, accountType) || other.accountType == accountType)&&(identical(other.ifscCode, ifscCode) || other.ifscCode == ifscCode)&&(identical(other.accountHolderName, accountHolderName) || other.accountHolderName == accountHolderName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,bankName,accountNumber,accountType,ifscCode,accountHolderName);

@override
String toString() {
  return 'AddBankAccountRequestDto(bankName: $bankName, accountNumber: $accountNumber, accountType: $accountType, ifscCode: $ifscCode, accountHolderName: $accountHolderName)';
}


}

/// @nodoc
abstract mixin class $AddBankAccountRequestDtoCopyWith<$Res>  {
  factory $AddBankAccountRequestDtoCopyWith(AddBankAccountRequestDto value, $Res Function(AddBankAccountRequestDto) _then) = _$AddBankAccountRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'bankName') String bankName,@JsonKey(name: 'accountNumber') String accountNumber,@JsonKey(name: 'accountType') String accountType,@JsonKey(name: 'ifscCode') String ifscCode,@JsonKey(name: 'accountHolderName') String accountHolderName
});




}
/// @nodoc
class _$AddBankAccountRequestDtoCopyWithImpl<$Res>
    implements $AddBankAccountRequestDtoCopyWith<$Res> {
  _$AddBankAccountRequestDtoCopyWithImpl(this._self, this._then);

  final AddBankAccountRequestDto _self;
  final $Res Function(AddBankAccountRequestDto) _then;

/// Create a copy of AddBankAccountRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? bankName = null,Object? accountNumber = null,Object? accountType = null,Object? ifscCode = null,Object? accountHolderName = null,}) {
  return _then(_self.copyWith(
bankName: null == bankName ? _self.bankName : bankName // ignore: cast_nullable_to_non_nullable
as String,accountNumber: null == accountNumber ? _self.accountNumber : accountNumber // ignore: cast_nullable_to_non_nullable
as String,accountType: null == accountType ? _self.accountType : accountType // ignore: cast_nullable_to_non_nullable
as String,ifscCode: null == ifscCode ? _self.ifscCode : ifscCode // ignore: cast_nullable_to_non_nullable
as String,accountHolderName: null == accountHolderName ? _self.accountHolderName : accountHolderName // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [AddBankAccountRequestDto].
extension AddBankAccountRequestDtoPatterns on AddBankAccountRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddBankAccountRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddBankAccountRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddBankAccountRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _AddBankAccountRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddBankAccountRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddBankAccountRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'bankName')  String bankName, @JsonKey(name: 'accountNumber')  String accountNumber, @JsonKey(name: 'accountType')  String accountType, @JsonKey(name: 'ifscCode')  String ifscCode, @JsonKey(name: 'accountHolderName')  String accountHolderName)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddBankAccountRequestDto() when $default != null:
return $default(_that.bankName,_that.accountNumber,_that.accountType,_that.ifscCode,_that.accountHolderName);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'bankName')  String bankName, @JsonKey(name: 'accountNumber')  String accountNumber, @JsonKey(name: 'accountType')  String accountType, @JsonKey(name: 'ifscCode')  String ifscCode, @JsonKey(name: 'accountHolderName')  String accountHolderName)  $default,) {final _that = this;
switch (_that) {
case _AddBankAccountRequestDto():
return $default(_that.bankName,_that.accountNumber,_that.accountType,_that.ifscCode,_that.accountHolderName);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'bankName')  String bankName, @JsonKey(name: 'accountNumber')  String accountNumber, @JsonKey(name: 'accountType')  String accountType, @JsonKey(name: 'ifscCode')  String ifscCode, @JsonKey(name: 'accountHolderName')  String accountHolderName)?  $default,) {final _that = this;
switch (_that) {
case _AddBankAccountRequestDto() when $default != null:
return $default(_that.bankName,_that.accountNumber,_that.accountType,_that.ifscCode,_that.accountHolderName);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddBankAccountRequestDto implements AddBankAccountRequestDto {
  const _AddBankAccountRequestDto({@JsonKey(name: 'bankName') required this.bankName, @JsonKey(name: 'accountNumber') required this.accountNumber, @JsonKey(name: 'accountType') required this.accountType, @JsonKey(name: 'ifscCode') required this.ifscCode, @JsonKey(name: 'accountHolderName') required this.accountHolderName});
  factory _AddBankAccountRequestDto.fromJson(Map<String, dynamic> json) => _$AddBankAccountRequestDtoFromJson(json);

@override@JsonKey(name: 'bankName') final  String bankName;
@override@JsonKey(name: 'accountNumber') final  String accountNumber;
@override@JsonKey(name: 'accountType') final  String accountType;
@override@JsonKey(name: 'ifscCode') final  String ifscCode;
@override@JsonKey(name: 'accountHolderName') final  String accountHolderName;

/// Create a copy of AddBankAccountRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddBankAccountRequestDtoCopyWith<_AddBankAccountRequestDto> get copyWith => __$AddBankAccountRequestDtoCopyWithImpl<_AddBankAccountRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddBankAccountRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddBankAccountRequestDto&&(identical(other.bankName, bankName) || other.bankName == bankName)&&(identical(other.accountNumber, accountNumber) || other.accountNumber == accountNumber)&&(identical(other.accountType, accountType) || other.accountType == accountType)&&(identical(other.ifscCode, ifscCode) || other.ifscCode == ifscCode)&&(identical(other.accountHolderName, accountHolderName) || other.accountHolderName == accountHolderName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,bankName,accountNumber,accountType,ifscCode,accountHolderName);

@override
String toString() {
  return 'AddBankAccountRequestDto(bankName: $bankName, accountNumber: $accountNumber, accountType: $accountType, ifscCode: $ifscCode, accountHolderName: $accountHolderName)';
}


}

/// @nodoc
abstract mixin class _$AddBankAccountRequestDtoCopyWith<$Res> implements $AddBankAccountRequestDtoCopyWith<$Res> {
  factory _$AddBankAccountRequestDtoCopyWith(_AddBankAccountRequestDto value, $Res Function(_AddBankAccountRequestDto) _then) = __$AddBankAccountRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'bankName') String bankName,@JsonKey(name: 'accountNumber') String accountNumber,@JsonKey(name: 'accountType') String accountType,@JsonKey(name: 'ifscCode') String ifscCode,@JsonKey(name: 'accountHolderName') String accountHolderName
});




}
/// @nodoc
class __$AddBankAccountRequestDtoCopyWithImpl<$Res>
    implements _$AddBankAccountRequestDtoCopyWith<$Res> {
  __$AddBankAccountRequestDtoCopyWithImpl(this._self, this._then);

  final _AddBankAccountRequestDto _self;
  final $Res Function(_AddBankAccountRequestDto) _then;

/// Create a copy of AddBankAccountRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? bankName = null,Object? accountNumber = null,Object? accountType = null,Object? ifscCode = null,Object? accountHolderName = null,}) {
  return _then(_AddBankAccountRequestDto(
bankName: null == bankName ? _self.bankName : bankName // ignore: cast_nullable_to_non_nullable
as String,accountNumber: null == accountNumber ? _self.accountNumber : accountNumber // ignore: cast_nullable_to_non_nullable
as String,accountType: null == accountType ? _self.accountType : accountType // ignore: cast_nullable_to_non_nullable
as String,ifscCode: null == ifscCode ? _self.ifscCode : ifscCode // ignore: cast_nullable_to_non_nullable
as String,accountHolderName: null == accountHolderName ? _self.accountHolderName : accountHolderName // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
