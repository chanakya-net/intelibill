// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'create_customer_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CreateCustomerRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'phoneNumber') String get phoneNumber;@JsonKey(name: 'address') String? get address;@JsonKey(name: 'isActive') bool get isActive;
/// Create a copy of CreateCustomerRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CreateCustomerRequestDtoCopyWith<CreateCustomerRequestDto> get copyWith => _$CreateCustomerRequestDtoCopyWithImpl<CreateCustomerRequestDto>(this as CreateCustomerRequestDto, _$identity);

  /// Serializes this CreateCustomerRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CreateCustomerRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.address, address) || other.address == address)&&(identical(other.isActive, isActive) || other.isActive == isActive));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,phoneNumber,address,isActive);

@override
String toString() {
  return 'CreateCustomerRequestDto(name: $name, phoneNumber: $phoneNumber, address: $address, isActive: $isActive)';
}


}

/// @nodoc
abstract mixin class $CreateCustomerRequestDtoCopyWith<$Res>  {
  factory $CreateCustomerRequestDtoCopyWith(CreateCustomerRequestDto value, $Res Function(CreateCustomerRequestDto) _then) = _$CreateCustomerRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'address') String? address,@JsonKey(name: 'isActive') bool isActive
});




}
/// @nodoc
class _$CreateCustomerRequestDtoCopyWithImpl<$Res>
    implements $CreateCustomerRequestDtoCopyWith<$Res> {
  _$CreateCustomerRequestDtoCopyWithImpl(this._self, this._then);

  final CreateCustomerRequestDto _self;
  final $Res Function(CreateCustomerRequestDto) _then;

/// Create a copy of CreateCustomerRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? phoneNumber = null,Object? address = freezed,Object? isActive = null,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,address: freezed == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String?,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [CreateCustomerRequestDto].
extension CreateCustomerRequestDtoPatterns on CreateCustomerRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CreateCustomerRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CreateCustomerRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CreateCustomerRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _CreateCustomerRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CreateCustomerRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _CreateCustomerRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'isActive')  bool isActive)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CreateCustomerRequestDto() when $default != null:
return $default(_that.name,_that.phoneNumber,_that.address,_that.isActive);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'isActive')  bool isActive)  $default,) {final _that = this;
switch (_that) {
case _CreateCustomerRequestDto():
return $default(_that.name,_that.phoneNumber,_that.address,_that.isActive);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'isActive')  bool isActive)?  $default,) {final _that = this;
switch (_that) {
case _CreateCustomerRequestDto() when $default != null:
return $default(_that.name,_that.phoneNumber,_that.address,_that.isActive);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CreateCustomerRequestDto implements CreateCustomerRequestDto {
  const _CreateCustomerRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'phoneNumber') required this.phoneNumber, @JsonKey(name: 'address') this.address, @JsonKey(name: 'isActive') required this.isActive});
  factory _CreateCustomerRequestDto.fromJson(Map<String, dynamic> json) => _$CreateCustomerRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'phoneNumber') final  String phoneNumber;
@override@JsonKey(name: 'address') final  String? address;
@override@JsonKey(name: 'isActive') final  bool isActive;

/// Create a copy of CreateCustomerRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CreateCustomerRequestDtoCopyWith<_CreateCustomerRequestDto> get copyWith => __$CreateCustomerRequestDtoCopyWithImpl<_CreateCustomerRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CreateCustomerRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CreateCustomerRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.address, address) || other.address == address)&&(identical(other.isActive, isActive) || other.isActive == isActive));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,phoneNumber,address,isActive);

@override
String toString() {
  return 'CreateCustomerRequestDto(name: $name, phoneNumber: $phoneNumber, address: $address, isActive: $isActive)';
}


}

/// @nodoc
abstract mixin class _$CreateCustomerRequestDtoCopyWith<$Res> implements $CreateCustomerRequestDtoCopyWith<$Res> {
  factory _$CreateCustomerRequestDtoCopyWith(_CreateCustomerRequestDto value, $Res Function(_CreateCustomerRequestDto) _then) = __$CreateCustomerRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'address') String? address,@JsonKey(name: 'isActive') bool isActive
});




}
/// @nodoc
class __$CreateCustomerRequestDtoCopyWithImpl<$Res>
    implements _$CreateCustomerRequestDtoCopyWith<$Res> {
  __$CreateCustomerRequestDtoCopyWithImpl(this._self, this._then);

  final _CreateCustomerRequestDto _self;
  final $Res Function(_CreateCustomerRequestDto) _then;

/// Create a copy of CreateCustomerRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? phoneNumber = null,Object? address = freezed,Object? isActive = null,}) {
  return _then(_CreateCustomerRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,address: freezed == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String?,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}

// dart format on
