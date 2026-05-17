// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'shop_details_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ShopDetailsDto {

@JsonKey(name: 'shopId') String get shopId;@JsonKey(name: 'name') String get name;@JsonKey(name: 'address') String get address;@JsonKey(name: 'city') String get city;@JsonKey(name: 'state') String get state;@JsonKey(name: 'pincode') String get pincode;@JsonKey(name: 'contactPerson') String? get contactPerson;@JsonKey(name: 'mobileNumber') String? get mobileNumber;@JsonKey(name: 'gstNumber') String? get gstNumber;@JsonKey(name: 'bankName') String? get bankName;@JsonKey(name: 'bankAccountNumber') String? get bankAccountNumber;@JsonKey(name: 'bankAccountType') String? get bankAccountType;@JsonKey(name: 'ifscCode') String? get ifscCode;@JsonKey(name: 'accountHolderName') String? get accountHolderName;
/// Create a copy of ShopDetailsDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ShopDetailsDtoCopyWith<ShopDetailsDto> get copyWith => _$ShopDetailsDtoCopyWithImpl<ShopDetailsDto>(this as ShopDetailsDto, _$identity);

  /// Serializes this ShopDetailsDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ShopDetailsDto&&(identical(other.shopId, shopId) || other.shopId == shopId)&&(identical(other.name, name) || other.name == name)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pincode, pincode) || other.pincode == pincode)&&(identical(other.contactPerson, contactPerson) || other.contactPerson == contactPerson)&&(identical(other.mobileNumber, mobileNumber) || other.mobileNumber == mobileNumber)&&(identical(other.gstNumber, gstNumber) || other.gstNumber == gstNumber)&&(identical(other.bankName, bankName) || other.bankName == bankName)&&(identical(other.bankAccountNumber, bankAccountNumber) || other.bankAccountNumber == bankAccountNumber)&&(identical(other.bankAccountType, bankAccountType) || other.bankAccountType == bankAccountType)&&(identical(other.ifscCode, ifscCode) || other.ifscCode == ifscCode)&&(identical(other.accountHolderName, accountHolderName) || other.accountHolderName == accountHolderName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,shopId,name,address,city,state,pincode,contactPerson,mobileNumber,gstNumber,bankName,bankAccountNumber,bankAccountType,ifscCode,accountHolderName);

@override
String toString() {
  return 'ShopDetailsDto(shopId: $shopId, name: $name, address: $address, city: $city, state: $state, pincode: $pincode, contactPerson: $contactPerson, mobileNumber: $mobileNumber, gstNumber: $gstNumber, bankName: $bankName, bankAccountNumber: $bankAccountNumber, bankAccountType: $bankAccountType, ifscCode: $ifscCode, accountHolderName: $accountHolderName)';
}


}

/// @nodoc
abstract mixin class $ShopDetailsDtoCopyWith<$Res>  {
  factory $ShopDetailsDtoCopyWith(ShopDetailsDto value, $Res Function(ShopDetailsDto) _then) = _$ShopDetailsDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'shopId') String shopId,@JsonKey(name: 'name') String name,@JsonKey(name: 'address') String address,@JsonKey(name: 'city') String city,@JsonKey(name: 'state') String state,@JsonKey(name: 'pincode') String pincode,@JsonKey(name: 'contactPerson') String? contactPerson,@JsonKey(name: 'mobileNumber') String? mobileNumber,@JsonKey(name: 'gstNumber') String? gstNumber,@JsonKey(name: 'bankName') String? bankName,@JsonKey(name: 'bankAccountNumber') String? bankAccountNumber,@JsonKey(name: 'bankAccountType') String? bankAccountType,@JsonKey(name: 'ifscCode') String? ifscCode,@JsonKey(name: 'accountHolderName') String? accountHolderName
});




}
/// @nodoc
class _$ShopDetailsDtoCopyWithImpl<$Res>
    implements $ShopDetailsDtoCopyWith<$Res> {
  _$ShopDetailsDtoCopyWithImpl(this._self, this._then);

  final ShopDetailsDto _self;
  final $Res Function(ShopDetailsDto) _then;

/// Create a copy of ShopDetailsDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? shopId = null,Object? name = null,Object? address = null,Object? city = null,Object? state = null,Object? pincode = null,Object? contactPerson = freezed,Object? mobileNumber = freezed,Object? gstNumber = freezed,Object? bankName = freezed,Object? bankAccountNumber = freezed,Object? bankAccountType = freezed,Object? ifscCode = freezed,Object? accountHolderName = freezed,}) {
  return _then(_self.copyWith(
shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,address: null == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String,city: null == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String,state: null == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String,pincode: null == pincode ? _self.pincode : pincode // ignore: cast_nullable_to_non_nullable
as String,contactPerson: freezed == contactPerson ? _self.contactPerson : contactPerson // ignore: cast_nullable_to_non_nullable
as String?,mobileNumber: freezed == mobileNumber ? _self.mobileNumber : mobileNumber // ignore: cast_nullable_to_non_nullable
as String?,gstNumber: freezed == gstNumber ? _self.gstNumber : gstNumber // ignore: cast_nullable_to_non_nullable
as String?,bankName: freezed == bankName ? _self.bankName : bankName // ignore: cast_nullable_to_non_nullable
as String?,bankAccountNumber: freezed == bankAccountNumber ? _self.bankAccountNumber : bankAccountNumber // ignore: cast_nullable_to_non_nullable
as String?,bankAccountType: freezed == bankAccountType ? _self.bankAccountType : bankAccountType // ignore: cast_nullable_to_non_nullable
as String?,ifscCode: freezed == ifscCode ? _self.ifscCode : ifscCode // ignore: cast_nullable_to_non_nullable
as String?,accountHolderName: freezed == accountHolderName ? _self.accountHolderName : accountHolderName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [ShopDetailsDto].
extension ShopDetailsDtoPatterns on ShopDetailsDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ShopDetailsDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ShopDetailsDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ShopDetailsDto value)  $default,){
final _that = this;
switch (_that) {
case _ShopDetailsDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ShopDetailsDto value)?  $default,){
final _that = this;
switch (_that) {
case _ShopDetailsDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopId')  String shopId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pincode')  String pincode, @JsonKey(name: 'contactPerson')  String? contactPerson, @JsonKey(name: 'mobileNumber')  String? mobileNumber, @JsonKey(name: 'gstNumber')  String? gstNumber, @JsonKey(name: 'bankName')  String? bankName, @JsonKey(name: 'bankAccountNumber')  String? bankAccountNumber, @JsonKey(name: 'bankAccountType')  String? bankAccountType, @JsonKey(name: 'ifscCode')  String? ifscCode, @JsonKey(name: 'accountHolderName')  String? accountHolderName)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ShopDetailsDto() when $default != null:
return $default(_that.shopId,_that.name,_that.address,_that.city,_that.state,_that.pincode,_that.contactPerson,_that.mobileNumber,_that.gstNumber,_that.bankName,_that.bankAccountNumber,_that.bankAccountType,_that.ifscCode,_that.accountHolderName);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'shopId')  String shopId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pincode')  String pincode, @JsonKey(name: 'contactPerson')  String? contactPerson, @JsonKey(name: 'mobileNumber')  String? mobileNumber, @JsonKey(name: 'gstNumber')  String? gstNumber, @JsonKey(name: 'bankName')  String? bankName, @JsonKey(name: 'bankAccountNumber')  String? bankAccountNumber, @JsonKey(name: 'bankAccountType')  String? bankAccountType, @JsonKey(name: 'ifscCode')  String? ifscCode, @JsonKey(name: 'accountHolderName')  String? accountHolderName)  $default,) {final _that = this;
switch (_that) {
case _ShopDetailsDto():
return $default(_that.shopId,_that.name,_that.address,_that.city,_that.state,_that.pincode,_that.contactPerson,_that.mobileNumber,_that.gstNumber,_that.bankName,_that.bankAccountNumber,_that.bankAccountType,_that.ifscCode,_that.accountHolderName);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'shopId')  String shopId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pincode')  String pincode, @JsonKey(name: 'contactPerson')  String? contactPerson, @JsonKey(name: 'mobileNumber')  String? mobileNumber, @JsonKey(name: 'gstNumber')  String? gstNumber, @JsonKey(name: 'bankName')  String? bankName, @JsonKey(name: 'bankAccountNumber')  String? bankAccountNumber, @JsonKey(name: 'bankAccountType')  String? bankAccountType, @JsonKey(name: 'ifscCode')  String? ifscCode, @JsonKey(name: 'accountHolderName')  String? accountHolderName)?  $default,) {final _that = this;
switch (_that) {
case _ShopDetailsDto() when $default != null:
return $default(_that.shopId,_that.name,_that.address,_that.city,_that.state,_that.pincode,_that.contactPerson,_that.mobileNumber,_that.gstNumber,_that.bankName,_that.bankAccountNumber,_that.bankAccountType,_that.ifscCode,_that.accountHolderName);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ShopDetailsDto implements ShopDetailsDto {
  const _ShopDetailsDto({@JsonKey(name: 'shopId') required this.shopId, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'address') required this.address, @JsonKey(name: 'city') required this.city, @JsonKey(name: 'state') required this.state, @JsonKey(name: 'pincode') required this.pincode, @JsonKey(name: 'contactPerson') this.contactPerson, @JsonKey(name: 'mobileNumber') this.mobileNumber, @JsonKey(name: 'gstNumber') this.gstNumber, @JsonKey(name: 'bankName') this.bankName, @JsonKey(name: 'bankAccountNumber') this.bankAccountNumber, @JsonKey(name: 'bankAccountType') this.bankAccountType, @JsonKey(name: 'ifscCode') this.ifscCode, @JsonKey(name: 'accountHolderName') this.accountHolderName});
  factory _ShopDetailsDto.fromJson(Map<String, dynamic> json) => _$ShopDetailsDtoFromJson(json);

@override@JsonKey(name: 'shopId') final  String shopId;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'address') final  String address;
@override@JsonKey(name: 'city') final  String city;
@override@JsonKey(name: 'state') final  String state;
@override@JsonKey(name: 'pincode') final  String pincode;
@override@JsonKey(name: 'contactPerson') final  String? contactPerson;
@override@JsonKey(name: 'mobileNumber') final  String? mobileNumber;
@override@JsonKey(name: 'gstNumber') final  String? gstNumber;
@override@JsonKey(name: 'bankName') final  String? bankName;
@override@JsonKey(name: 'bankAccountNumber') final  String? bankAccountNumber;
@override@JsonKey(name: 'bankAccountType') final  String? bankAccountType;
@override@JsonKey(name: 'ifscCode') final  String? ifscCode;
@override@JsonKey(name: 'accountHolderName') final  String? accountHolderName;

/// Create a copy of ShopDetailsDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ShopDetailsDtoCopyWith<_ShopDetailsDto> get copyWith => __$ShopDetailsDtoCopyWithImpl<_ShopDetailsDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ShopDetailsDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ShopDetailsDto&&(identical(other.shopId, shopId) || other.shopId == shopId)&&(identical(other.name, name) || other.name == name)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pincode, pincode) || other.pincode == pincode)&&(identical(other.contactPerson, contactPerson) || other.contactPerson == contactPerson)&&(identical(other.mobileNumber, mobileNumber) || other.mobileNumber == mobileNumber)&&(identical(other.gstNumber, gstNumber) || other.gstNumber == gstNumber)&&(identical(other.bankName, bankName) || other.bankName == bankName)&&(identical(other.bankAccountNumber, bankAccountNumber) || other.bankAccountNumber == bankAccountNumber)&&(identical(other.bankAccountType, bankAccountType) || other.bankAccountType == bankAccountType)&&(identical(other.ifscCode, ifscCode) || other.ifscCode == ifscCode)&&(identical(other.accountHolderName, accountHolderName) || other.accountHolderName == accountHolderName));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,shopId,name,address,city,state,pincode,contactPerson,mobileNumber,gstNumber,bankName,bankAccountNumber,bankAccountType,ifscCode,accountHolderName);

@override
String toString() {
  return 'ShopDetailsDto(shopId: $shopId, name: $name, address: $address, city: $city, state: $state, pincode: $pincode, contactPerson: $contactPerson, mobileNumber: $mobileNumber, gstNumber: $gstNumber, bankName: $bankName, bankAccountNumber: $bankAccountNumber, bankAccountType: $bankAccountType, ifscCode: $ifscCode, accountHolderName: $accountHolderName)';
}


}

/// @nodoc
abstract mixin class _$ShopDetailsDtoCopyWith<$Res> implements $ShopDetailsDtoCopyWith<$Res> {
  factory _$ShopDetailsDtoCopyWith(_ShopDetailsDto value, $Res Function(_ShopDetailsDto) _then) = __$ShopDetailsDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'shopId') String shopId,@JsonKey(name: 'name') String name,@JsonKey(name: 'address') String address,@JsonKey(name: 'city') String city,@JsonKey(name: 'state') String state,@JsonKey(name: 'pincode') String pincode,@JsonKey(name: 'contactPerson') String? contactPerson,@JsonKey(name: 'mobileNumber') String? mobileNumber,@JsonKey(name: 'gstNumber') String? gstNumber,@JsonKey(name: 'bankName') String? bankName,@JsonKey(name: 'bankAccountNumber') String? bankAccountNumber,@JsonKey(name: 'bankAccountType') String? bankAccountType,@JsonKey(name: 'ifscCode') String? ifscCode,@JsonKey(name: 'accountHolderName') String? accountHolderName
});




}
/// @nodoc
class __$ShopDetailsDtoCopyWithImpl<$Res>
    implements _$ShopDetailsDtoCopyWith<$Res> {
  __$ShopDetailsDtoCopyWithImpl(this._self, this._then);

  final _ShopDetailsDto _self;
  final $Res Function(_ShopDetailsDto) _then;

/// Create a copy of ShopDetailsDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? shopId = null,Object? name = null,Object? address = null,Object? city = null,Object? state = null,Object? pincode = null,Object? contactPerson = freezed,Object? mobileNumber = freezed,Object? gstNumber = freezed,Object? bankName = freezed,Object? bankAccountNumber = freezed,Object? bankAccountType = freezed,Object? ifscCode = freezed,Object? accountHolderName = freezed,}) {
  return _then(_ShopDetailsDto(
shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,address: null == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String,city: null == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String,state: null == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String,pincode: null == pincode ? _self.pincode : pincode // ignore: cast_nullable_to_non_nullable
as String,contactPerson: freezed == contactPerson ? _self.contactPerson : contactPerson // ignore: cast_nullable_to_non_nullable
as String?,mobileNumber: freezed == mobileNumber ? _self.mobileNumber : mobileNumber // ignore: cast_nullable_to_non_nullable
as String?,gstNumber: freezed == gstNumber ? _self.gstNumber : gstNumber // ignore: cast_nullable_to_non_nullable
as String?,bankName: freezed == bankName ? _self.bankName : bankName // ignore: cast_nullable_to_non_nullable
as String?,bankAccountNumber: freezed == bankAccountNumber ? _self.bankAccountNumber : bankAccountNumber // ignore: cast_nullable_to_non_nullable
as String?,bankAccountType: freezed == bankAccountType ? _self.bankAccountType : bankAccountType // ignore: cast_nullable_to_non_nullable
as String?,ifscCode: freezed == ifscCode ? _self.ifscCode : ifscCode // ignore: cast_nullable_to_non_nullable
as String?,accountHolderName: freezed == accountHolderName ? _self.accountHolderName : accountHolderName // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
