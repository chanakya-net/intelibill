// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'supplier_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SupplierDto {

@JsonKey(name: 'supplierId') String get supplierId;@JsonKey(name: 'name') String get name;@JsonKey(name: 'contactPersonName') String? get contactPersonName;@JsonKey(name: 'contactPersonPhone') String? get contactPersonPhone;@JsonKey(name: 'address') String? get address;@JsonKey(name: 'city') String? get city;@JsonKey(name: 'state') String? get state;@JsonKey(name: 'pin') String? get pin;@JsonKey(name: 'isSystem') bool get isSystem;@JsonKey(name: 'isActive') bool get isActive;@JsonKey(name: 'isPreferred') bool get isPreferred;@JsonKey(name: 'balanceDue') double get balanceDue;
/// Create a copy of SupplierDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SupplierDtoCopyWith<SupplierDto> get copyWith => _$SupplierDtoCopyWithImpl<SupplierDto>(this as SupplierDto, _$identity);

  /// Serializes this SupplierDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SupplierDto&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.name, name) || other.name == name)&&(identical(other.contactPersonName, contactPersonName) || other.contactPersonName == contactPersonName)&&(identical(other.contactPersonPhone, contactPersonPhone) || other.contactPersonPhone == contactPersonPhone)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pin, pin) || other.pin == pin)&&(identical(other.isSystem, isSystem) || other.isSystem == isSystem)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.isPreferred, isPreferred) || other.isPreferred == isPreferred)&&(identical(other.balanceDue, balanceDue) || other.balanceDue == balanceDue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,supplierId,name,contactPersonName,contactPersonPhone,address,city,state,pin,isSystem,isActive,isPreferred,balanceDue);

@override
String toString() {
  return 'SupplierDto(supplierId: $supplierId, name: $name, contactPersonName: $contactPersonName, contactPersonPhone: $contactPersonPhone, address: $address, city: $city, state: $state, pin: $pin, isSystem: $isSystem, isActive: $isActive, isPreferred: $isPreferred, balanceDue: $balanceDue)';
}


}

/// @nodoc
abstract mixin class $SupplierDtoCopyWith<$Res>  {
  factory $SupplierDtoCopyWith(SupplierDto value, $Res Function(SupplierDto) _then) = _$SupplierDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'supplierId') String supplierId,@JsonKey(name: 'name') String name,@JsonKey(name: 'contactPersonName') String? contactPersonName,@JsonKey(name: 'contactPersonPhone') String? contactPersonPhone,@JsonKey(name: 'address') String? address,@JsonKey(name: 'city') String? city,@JsonKey(name: 'state') String? state,@JsonKey(name: 'pin') String? pin,@JsonKey(name: 'isSystem') bool isSystem,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'isPreferred') bool isPreferred,@JsonKey(name: 'balanceDue') double balanceDue
});




}
/// @nodoc
class _$SupplierDtoCopyWithImpl<$Res>
    implements $SupplierDtoCopyWith<$Res> {
  _$SupplierDtoCopyWithImpl(this._self, this._then);

  final SupplierDto _self;
  final $Res Function(SupplierDto) _then;

/// Create a copy of SupplierDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? supplierId = null,Object? name = null,Object? contactPersonName = freezed,Object? contactPersonPhone = freezed,Object? address = freezed,Object? city = freezed,Object? state = freezed,Object? pin = freezed,Object? isSystem = null,Object? isActive = null,Object? isPreferred = null,Object? balanceDue = null,}) {
  return _then(_self.copyWith(
supplierId: null == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,contactPersonName: freezed == contactPersonName ? _self.contactPersonName : contactPersonName // ignore: cast_nullable_to_non_nullable
as String?,contactPersonPhone: freezed == contactPersonPhone ? _self.contactPersonPhone : contactPersonPhone // ignore: cast_nullable_to_non_nullable
as String?,address: freezed == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String?,city: freezed == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String?,state: freezed == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String?,pin: freezed == pin ? _self.pin : pin // ignore: cast_nullable_to_non_nullable
as String?,isSystem: null == isSystem ? _self.isSystem : isSystem // ignore: cast_nullable_to_non_nullable
as bool,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,isPreferred: null == isPreferred ? _self.isPreferred : isPreferred // ignore: cast_nullable_to_non_nullable
as bool,balanceDue: null == balanceDue ? _self.balanceDue : balanceDue // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SupplierDto].
extension SupplierDtoPatterns on SupplierDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SupplierDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SupplierDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SupplierDto value)  $default,){
final _that = this;
switch (_that) {
case _SupplierDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SupplierDto value)?  $default,){
final _that = this;
switch (_that) {
case _SupplierDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'supplierId')  String supplierId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'contactPersonName')  String? contactPersonName, @JsonKey(name: 'contactPersonPhone')  String? contactPersonPhone, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'city')  String? city, @JsonKey(name: 'state')  String? state, @JsonKey(name: 'pin')  String? pin, @JsonKey(name: 'isSystem')  bool isSystem, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'isPreferred')  bool isPreferred, @JsonKey(name: 'balanceDue')  double balanceDue)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SupplierDto() when $default != null:
return $default(_that.supplierId,_that.name,_that.contactPersonName,_that.contactPersonPhone,_that.address,_that.city,_that.state,_that.pin,_that.isSystem,_that.isActive,_that.isPreferred,_that.balanceDue);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'supplierId')  String supplierId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'contactPersonName')  String? contactPersonName, @JsonKey(name: 'contactPersonPhone')  String? contactPersonPhone, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'city')  String? city, @JsonKey(name: 'state')  String? state, @JsonKey(name: 'pin')  String? pin, @JsonKey(name: 'isSystem')  bool isSystem, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'isPreferred')  bool isPreferred, @JsonKey(name: 'balanceDue')  double balanceDue)  $default,) {final _that = this;
switch (_that) {
case _SupplierDto():
return $default(_that.supplierId,_that.name,_that.contactPersonName,_that.contactPersonPhone,_that.address,_that.city,_that.state,_that.pin,_that.isSystem,_that.isActive,_that.isPreferred,_that.balanceDue);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'supplierId')  String supplierId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'contactPersonName')  String? contactPersonName, @JsonKey(name: 'contactPersonPhone')  String? contactPersonPhone, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'city')  String? city, @JsonKey(name: 'state')  String? state, @JsonKey(name: 'pin')  String? pin, @JsonKey(name: 'isSystem')  bool isSystem, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'isPreferred')  bool isPreferred, @JsonKey(name: 'balanceDue')  double balanceDue)?  $default,) {final _that = this;
switch (_that) {
case _SupplierDto() when $default != null:
return $default(_that.supplierId,_that.name,_that.contactPersonName,_that.contactPersonPhone,_that.address,_that.city,_that.state,_that.pin,_that.isSystem,_that.isActive,_that.isPreferred,_that.balanceDue);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SupplierDto implements SupplierDto {
  const _SupplierDto({@JsonKey(name: 'supplierId') required this.supplierId, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'contactPersonName') this.contactPersonName, @JsonKey(name: 'contactPersonPhone') this.contactPersonPhone, @JsonKey(name: 'address') this.address, @JsonKey(name: 'city') this.city, @JsonKey(name: 'state') this.state, @JsonKey(name: 'pin') this.pin, @JsonKey(name: 'isSystem') required this.isSystem, @JsonKey(name: 'isActive') required this.isActive, @JsonKey(name: 'isPreferred') required this.isPreferred, @JsonKey(name: 'balanceDue') this.balanceDue = 0.0});
  factory _SupplierDto.fromJson(Map<String, dynamic> json) => _$SupplierDtoFromJson(json);

@override@JsonKey(name: 'supplierId') final  String supplierId;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'contactPersonName') final  String? contactPersonName;
@override@JsonKey(name: 'contactPersonPhone') final  String? contactPersonPhone;
@override@JsonKey(name: 'address') final  String? address;
@override@JsonKey(name: 'city') final  String? city;
@override@JsonKey(name: 'state') final  String? state;
@override@JsonKey(name: 'pin') final  String? pin;
@override@JsonKey(name: 'isSystem') final  bool isSystem;
@override@JsonKey(name: 'isActive') final  bool isActive;
@override@JsonKey(name: 'isPreferred') final  bool isPreferred;
@override@JsonKey(name: 'balanceDue') final  double balanceDue;

/// Create a copy of SupplierDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SupplierDtoCopyWith<_SupplierDto> get copyWith => __$SupplierDtoCopyWithImpl<_SupplierDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SupplierDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SupplierDto&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.name, name) || other.name == name)&&(identical(other.contactPersonName, contactPersonName) || other.contactPersonName == contactPersonName)&&(identical(other.contactPersonPhone, contactPersonPhone) || other.contactPersonPhone == contactPersonPhone)&&(identical(other.address, address) || other.address == address)&&(identical(other.city, city) || other.city == city)&&(identical(other.state, state) || other.state == state)&&(identical(other.pin, pin) || other.pin == pin)&&(identical(other.isSystem, isSystem) || other.isSystem == isSystem)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.isPreferred, isPreferred) || other.isPreferred == isPreferred)&&(identical(other.balanceDue, balanceDue) || other.balanceDue == balanceDue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,supplierId,name,contactPersonName,contactPersonPhone,address,city,state,pin,isSystem,isActive,isPreferred,balanceDue);

@override
String toString() {
  return 'SupplierDto(supplierId: $supplierId, name: $name, contactPersonName: $contactPersonName, contactPersonPhone: $contactPersonPhone, address: $address, city: $city, state: $state, pin: $pin, isSystem: $isSystem, isActive: $isActive, isPreferred: $isPreferred, balanceDue: $balanceDue)';
}


}

/// @nodoc
abstract mixin class _$SupplierDtoCopyWith<$Res> implements $SupplierDtoCopyWith<$Res> {
  factory _$SupplierDtoCopyWith(_SupplierDto value, $Res Function(_SupplierDto) _then) = __$SupplierDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'supplierId') String supplierId,@JsonKey(name: 'name') String name,@JsonKey(name: 'contactPersonName') String? contactPersonName,@JsonKey(name: 'contactPersonPhone') String? contactPersonPhone,@JsonKey(name: 'address') String? address,@JsonKey(name: 'city') String? city,@JsonKey(name: 'state') String? state,@JsonKey(name: 'pin') String? pin,@JsonKey(name: 'isSystem') bool isSystem,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'isPreferred') bool isPreferred,@JsonKey(name: 'balanceDue') double balanceDue
});




}
/// @nodoc
class __$SupplierDtoCopyWithImpl<$Res>
    implements _$SupplierDtoCopyWith<$Res> {
  __$SupplierDtoCopyWithImpl(this._self, this._then);

  final _SupplierDto _self;
  final $Res Function(_SupplierDto) _then;

/// Create a copy of SupplierDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? supplierId = null,Object? name = null,Object? contactPersonName = freezed,Object? contactPersonPhone = freezed,Object? address = freezed,Object? city = freezed,Object? state = freezed,Object? pin = freezed,Object? isSystem = null,Object? isActive = null,Object? isPreferred = null,Object? balanceDue = null,}) {
  return _then(_SupplierDto(
supplierId: null == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,contactPersonName: freezed == contactPersonName ? _self.contactPersonName : contactPersonName // ignore: cast_nullable_to_non_nullable
as String?,contactPersonPhone: freezed == contactPersonPhone ? _self.contactPersonPhone : contactPersonPhone // ignore: cast_nullable_to_non_nullable
as String?,address: freezed == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String?,city: freezed == city ? _self.city : city // ignore: cast_nullable_to_non_nullable
as String?,state: freezed == state ? _self.state : state // ignore: cast_nullable_to_non_nullable
as String?,pin: freezed == pin ? _self.pin : pin // ignore: cast_nullable_to_non_nullable
as String?,isSystem: null == isSystem ? _self.isSystem : isSystem // ignore: cast_nullable_to_non_nullable
as bool,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,isPreferred: null == isPreferred ? _self.isPreferred : isPreferred // ignore: cast_nullable_to_non_nullable
as bool,balanceDue: null == balanceDue ? _self.balanceDue : balanceDue // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
