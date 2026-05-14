// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'create_supplier_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CreateSupplierRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'contactPersonName') String? get contactPersonName;@JsonKey(name: 'contactPersonPhone') String? get contactPersonPhone;@JsonKey(name: 'address') String get address;@JsonKey(name: 'city') String get city;@JsonKey(name: 'state') String get state;@JsonKey(name: 'pin') String get pin;@JsonKey(name: 'isActive') bool get isActive;@JsonKey(name: 'isPreferred') bool get isPreferred;
/// Create a copy of CreateSupplierRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreateSupplierRequestDtoCopyWith<CreateSupplierRequestDto> get copyWith => _$CreateSupplierRequestDtoCopyWithImpl<CreateSupplierRequestDto>(this as CreateSupplierRequestDto, _$identity);

  /// Serializes this CreateSupplierRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreateSupplierRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.contactPersonName, contactPersonName) || other.contactPersonName == contactPersonName)&&(identical(other.contactPersonPhone, contactPersonPhone) || other.contactPersonPhone == contactPersonPhone)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pin, pin) || other.pin == pin)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.isPreferred, isPreferred) || other.isPreferred == isPreferred));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,contactPersonName,contactPersonPhone,address,city,state,pin,isActive,isPreferred);

@override
String toString() {
  return 'CreateSupplierRequestDto(name: $name, contactPersonName: $contactPersonName, contactPersonPhone: $contactPersonPhone, address: $address, city: $city, state: $state, pin: $pin, isActive: $isActive, isPreferred: $isPreferred)';
}


}

/// @nodoc
abstract mixin class $CreateSupplierRequestDtoCopyWith<$Res>  {
  factory $CreateSupplierRequestDtoCopyWith(CreateSupplierRequestDto value, $Res Function(CreateSupplierRequestDto) _then) = _$CreateSupplierRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'contactPersonName') String? contactPersonName,@JsonKey(name: 'contactPersonPhone') String? contactPersonPhone,@JsonKey(name: 'address') String address,@JsonKey(name: 'city') String city,@JsonKey(name: 'state') String state,@JsonKey(name: 'pin') String pin,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'isPreferred') bool isPreferred
});




}
/// @nodoc
class _$CreateSupplierRequestDtoCopyWithImpl<$Res>
    implements $CreateSupplierRequestDtoCopyWith<$Res> {
  _$CreateSupplierRequestDtoCopyWithImpl(this._self, this._then);

  final CreateSupplierRequestDto _self;
  final $Res Function(CreateSupplierRequestDto) _then;

/// Create a copy of CreateSupplierRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? contactPersonName = freezed,Object? contactPersonPhone = freezed,Object? address = null,Object? city = null,Object? state = null,Object? pin = null,Object? isActive = null,Object? isPreferred = null,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,contactPersonName: freezed == contactPersonName ? _self.contactPersonName : contactPersonName // ignore: cast_nullable_to_non_nullable
as String?,contactPersonPhone: freezed == contactPersonPhone ? _self.contactPersonPhone : contactPersonPhone // ignore: cast_nullable_to_non_nullable
as String?,address: null == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String,city: null == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String,state: null == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String,pin: null == pin ? _self.pin : pin // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,isPreferred: null == isPreferred ? _self.isPreferred : isPreferred // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [CreateSupplierRequestDto].
extension CreateSupplierRequestDtoPatterns on CreateSupplierRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreateSupplierRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreateSupplierRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreateSupplierRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _CreateSupplierRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreateSupplierRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreateSupplierRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'contactPersonName')  String? contactPersonName, @JsonKey(name: 'contactPersonPhone')  String? contactPersonPhone, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pin')  String pin, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'isPreferred')  bool isPreferred)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreateSupplierRequestDto() when $default != null:
return $default(_that.name,_that.contactPersonName,_that.contactPersonPhone,_that.address,_that.city,_that.state,_that.pin,_that.isActive,_that.isPreferred);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'contactPersonName')  String? contactPersonName, @JsonKey(name: 'contactPersonPhone')  String? contactPersonPhone, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pin')  String pin, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'isPreferred')  bool isPreferred)  $default,) {final _that = this;
switch (_that) {
case _CreateSupplierRequestDto():
return $default(_that.name,_that.contactPersonName,_that.contactPersonPhone,_that.address,_that.city,_that.state,_that.pin,_that.isActive,_that.isPreferred);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'contactPersonName')  String? contactPersonName, @JsonKey(name: 'contactPersonPhone')  String? contactPersonPhone, @JsonKey(name: 'address')  String address, @JsonKey(name: 'city')  String city, @JsonKey(name: 'state')  String state, @JsonKey(name: 'pin')  String pin, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'isPreferred')  bool isPreferred)?  $default,) {final _that = this;
switch (_that) {
case _CreateSupplierRequestDto() when $default != null:
return $default(_that.name,_that.contactPersonName,_that.contactPersonPhone,_that.address,_that.city,_that.state,_that.pin,_that.isActive,_that.isPreferred);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreateSupplierRequestDto implements CreateSupplierRequestDto {
  const _CreateSupplierRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'contactPersonName') this.contactPersonName, @JsonKey(name: 'contactPersonPhone') this.contactPersonPhone, @JsonKey(name: 'address') required this.address, @JsonKey(name: 'city') required this.city, @JsonKey(name: 'state') required this.state, @JsonKey(name: 'pin') required this.pin, @JsonKey(name: 'isActive') required this.isActive, @JsonKey(name: 'isPreferred') required this.isPreferred});
  factory _CreateSupplierRequestDto.fromJson(Map<String, dynamic> json) => _$CreateSupplierRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'contactPersonName') final  String? contactPersonName;
@override@JsonKey(name: 'contactPersonPhone') final  String? contactPersonPhone;
@override@JsonKey(name: 'address') final  String address;
@override@JsonKey(name: 'city') final  String city;
@override@JsonKey(name: 'state') final  String state;
@override@JsonKey(name: 'pin') final  String pin;
@override@JsonKey(name: 'isActive') final  bool isActive;
@override@JsonKey(name: 'isPreferred') final  bool isPreferred;

/// Create a copy of CreateSupplierRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreateSupplierRequestDtoCopyWith<_CreateSupplierRequestDto> get copyWith => __$CreateSupplierRequestDtoCopyWithImpl<_CreateSupplierRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreateSupplierRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreateSupplierRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.contactPersonName, contactPersonName) || other.contactPersonName == contactPersonName)&&(identical(other.contactPersonPhone, contactPersonPhone) || other.contactPersonPhone == contactPersonPhone)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pin, pin) || other.pin == pin)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.isPreferred, isPreferred) || other.isPreferred == isPreferred));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,contactPersonName,contactPersonPhone,address,city,state,pin,isActive,isPreferred);

@override
String toString() {
  return 'CreateSupplierRequestDto(name: $name, contactPersonName: $contactPersonName, contactPersonPhone: $contactPersonPhone, address: $address, city: $city, state: $state, pin: $pin, isActive: $isActive, isPreferred: $isPreferred)';
}


}

/// @nodoc
abstract mixin class _$CreateSupplierRequestDtoCopyWith<$Res> implements $CreateSupplierRequestDtoCopyWith<$Res> {
  factory _$CreateSupplierRequestDtoCopyWith(_CreateSupplierRequestDto value, $Res Function(_CreateSupplierRequestDto) _then) = __$CreateSupplierRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'contactPersonName') String? contactPersonName,@JsonKey(name: 'contactPersonPhone') String? contactPersonPhone,@JsonKey(name: 'address') String address,@JsonKey(name: 'city') String city,@JsonKey(name: 'state') String state,@JsonKey(name: 'pin') String pin,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'isPreferred') bool isPreferred
});




}
/// @nodoc
class __$CreateSupplierRequestDtoCopyWithImpl<$Res>
    implements _$CreateSupplierRequestDtoCopyWith<$Res> {
  __$CreateSupplierRequestDtoCopyWithImpl(this._self, this._then);

  final _CreateSupplierRequestDto _self;
  final $Res Function(_CreateSupplierRequestDto) _then;

/// Create a copy of CreateSupplierRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? contactPersonName = freezed,Object? contactPersonPhone = freezed,Object? address = null,Object? city = null,Object? state = null,Object? pin = null,Object? isActive = null,Object? isPreferred = null,}) {
  return _then(_CreateSupplierRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,contactPersonName: freezed == contactPersonName ? _self.contactPersonName : contactPersonName // ignore: cast_nullable_to_non_nullable
as String?,contactPersonPhone: freezed == contactPersonPhone ? _self.contactPersonPhone : contactPersonPhone // ignore: cast_nullable_to_non_nullable
as String?,address: null == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String,city: null == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String,state: null == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String,pin: null == pin ? _self.pin : pin // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,isPreferred: null == isPreferred ? _self.isPreferred : isPreferred // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}

// dart format on
