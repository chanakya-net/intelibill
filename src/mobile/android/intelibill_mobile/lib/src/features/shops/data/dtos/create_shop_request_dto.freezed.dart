// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'create_shop_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CreateShopRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'address') String get address;@JsonKey(name: 'city') String get city;@JsonKey(name: 'state') String get state;@JsonKey(name: 'pincode') String get pincode;@JsonKey(name: 'contactPerson') String? get contactPerson;@JsonKey(name: 'mobileNumber') String? get mobileNumber;@JsonKey(name: 'gstNumber') String? get gstNumber;
/// Create a copy of CreateShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreateShopRequestDtoCopyWith<CreateShopRequestDto> get copyWith => _$CreateShopRequestDtoCopyWithImpl<CreateShopRequestDto>(this as CreateShopRequestDto, _$identity);

  /// Serializes this CreateShopRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreateShopRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pincode, pincode) || other.pincode == pincode)&&(identical(other.contactPerson, contactPerson) || other.contactPerson == contactPerson)&&(identical(other.mobileNumber, mobileNumber) || other.mobileNumber == mobileNumber)&&(identical(other.gstNumber, gstNumber) || other.gstNumber == gstNumber));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,address,city,state,pincode,contactPerson,mobileNumber,gstNumber);

@override
String toString() {
  return 'CreateShopRequestDto(name: $name, address: $address, city: $city, state: $state, pincode: $pincode, contactPerson: $contactPerson, mobileNumber: $mobileNumber, gstNumber: $gstNumber)';
}


}

/// @nodoc
abstract mixin class $CreateShopRequestDtoCopyWith<$Res>  {
  factory $CreateShopRequestDtoCopyWith(CreateShopRequestDto value, $Res Function(CreateShopRequestDto) _then) = _$CreateShopRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'address') String address,@JsonKey(name: 'city') String city,@JsonKey(name: 'state') String state,@JsonKey(name: 'pincode') String pincode,@JsonKey(name: 'contactPerson') String? contactPerson,@JsonKey(name: 'mobileNumber') String? mobileNumber,@JsonKey(name: 'gstNumber') String? gstNumber
});




}
/// @nodoc
class _$CreateShopRequestDtoCopyWithImpl<$Res>
    implements $CreateShopRequestDtoCopyWith<$Res> {
  _$CreateShopRequestDtoCopyWithImpl(this._self, this._then);

  final CreateShopRequestDto _self;
  final $Res Function(CreateShopRequestDto) _then;

/// Create a copy of CreateShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? address = null,Object? city = null,Object? state = null,Object? pincode = null,Object? contactPerson = freezed,Object? mobileNumber = freezed,Object? gstNumber = freezed,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,address: null == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String,city: null == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String,state: null == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String,pincode: null == pincode ? _self.pincode : pincode // ignore: cast_nullable_to_non_nullable
as String,contactPerson: freezed == contactPerson ? _self.contactPerson : contactPerson // ignore: cast_nullable_to_non_nullable
as String?,mobileNumber: freezed == mobileNumber ? _self.mobileNumber : mobileNumber // ignore: cast_nullable_to_non_nullable
as String?,gstNumber: freezed == gstNumber ? _self.gstNumber : gstNumber // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}

}


/// Adds pattern-matching-related methods to [CreateShopRequestDto].
extension CreateShopRequestDtoPatterns on CreateShopRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreateShopRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreateShopRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreateShopRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _CreateShopRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreateShopRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreateShopRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pincode')  String pincode, @JsonKey(name: 'contactPerson')  String? contactPerson, @JsonKey(name: 'mobileNumber')  String? mobileNumber, @JsonKey(name: 'gstNumber')  String? gstNumber)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreateShopRequestDto() when $default != null:
return $default(_that.name,_that.address,_that.city,_that.state,_that.pincode,_that.contactPerson,_that.mobileNumber,_that.gstNumber);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pincode')  String pincode, @JsonKey(name: 'contactPerson')  String? contactPerson, @JsonKey(name: 'mobileNumber')  String? mobileNumber, @JsonKey(name: 'gstNumber')  String? gstNumber)  $default,) {final _that = this;
switch (_that) {
case _CreateShopRequestDto():
return $default(_that.name,_that.address,_that.city,_that.state,_that.pincode,_that.contactPerson,_that.mobileNumber,_that.gstNumber);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pincode')  String pincode, @JsonKey(name: 'contactPerson')  String? contactPerson, @JsonKey(name: 'mobileNumber')  String? mobileNumber, @JsonKey(name: 'gstNumber')  String? gstNumber)?  $default,) {final _that = this;
switch (_that) {
case _CreateShopRequestDto() when $default != null:
return $default(_that.name,_that.address,_that.city,_that.state,_that.pincode,_that.contactPerson,_that.mobileNumber,_that.gstNumber);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreateShopRequestDto implements CreateShopRequestDto {
  const _CreateShopRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'address') required this.address, @JsonKey(name: 'city') required this.city, @JsonKey(name: 'state') required this.state, @JsonKey(name: 'pincode') required this.pincode, @JsonKey(name: 'contactPerson') this.contactPerson, @JsonKey(name: 'mobileNumber') this.mobileNumber, @JsonKey(name: 'gstNumber') this.gstNumber});
  factory _CreateShopRequestDto.fromJson(Map<String, dynamic> json) => _$CreateShopRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'address') final  String address;
@override@JsonKey(name: 'city') final  String city;
@override@JsonKey(name: 'state') final  String state;
@override@JsonKey(name: 'pincode') final  String pincode;
@override@JsonKey(name: 'contactPerson') final  String? contactPerson;
@override@JsonKey(name: 'mobileNumber') final  String? mobileNumber;
@override@JsonKey(name: 'gstNumber') final  String? gstNumber;

/// Create a copy of CreateShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreateShopRequestDtoCopyWith<_CreateShopRequestDto> get copyWith => __$CreateShopRequestDtoCopyWithImpl<_CreateShopRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreateShopRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreateShopRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pincode, pincode) || other.pincode == pincode)&&(identical(other.contactPerson, contactPerson) || other.contactPerson == contactPerson)&&(identical(other.mobileNumber, mobileNumber) || other.mobileNumber == mobileNumber)&&(identical(other.gstNumber, gstNumber) || other.gstNumber == gstNumber));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,address,city,state,pincode,contactPerson,mobileNumber,gstNumber);

@override
String toString() {
  return 'CreateShopRequestDto(name: $name, address: $address, city: $city, state: $state, pincode: $pincode, contactPerson: $contactPerson, mobileNumber: $mobileNumber, gstNumber: $gstNumber)';
}


}

/// @nodoc
abstract mixin class _$CreateShopRequestDtoCopyWith<$Res> implements $CreateShopRequestDtoCopyWith<$Res> {
  factory _$CreateShopRequestDtoCopyWith(_CreateShopRequestDto value, $Res Function(_CreateShopRequestDto) _then) = __$CreateShopRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'address') String address,@JsonKey(name: 'city') String city,@JsonKey(name: 'state') String state,@JsonKey(name: 'pincode') String pincode,@JsonKey(name: 'contactPerson') String? contactPerson,@JsonKey(name: 'mobileNumber') String? mobileNumber,@JsonKey(name: 'gstNumber') String? gstNumber
});




}
/// @nodoc
class __$CreateShopRequestDtoCopyWithImpl<$Res>
    implements _$CreateShopRequestDtoCopyWith<$Res> {
  __$CreateShopRequestDtoCopyWithImpl(this._self, this._then);

  final _CreateShopRequestDto _self;
  final $Res Function(_CreateShopRequestDto) _then;

/// Create a copy of CreateShopRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? address = null,Object? city = null,Object? state = null,Object? pincode = null,Object? contactPerson = freezed,Object? mobileNumber = freezed,Object? gstNumber = freezed,}) {
  return _then(_CreateShopRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,address: null == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String,city: null == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String,state: null == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String,pincode: null == pincode ? _self.pincode : pincode // ignore: cast_nullable_to_non_nullable
as String,contactPerson: freezed == contactPerson ? _self.contactPerson : contactPerson // ignore: cast_nullable_to_non_nullable
as String?,mobileNumber: freezed == mobileNumber ? _self.mobileNumber : mobileNumber // ignore: cast_nullable_to_non_nullable
as String?,gstNumber: freezed == gstNumber ? _self.gstNumber : gstNumber // ignore: cast_nullable_to_non_nullable
as String?,
  ));
}


}

// dart format on
