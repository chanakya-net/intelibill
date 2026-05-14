// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'customer_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$CustomerDto {

@JsonKey(name: 'customerId') String get customerId;@JsonKey(name: 'name') String get name;@JsonKey(name: 'phoneNumber') String get phoneNumber;@JsonKey(name: 'address') String? get address;@JsonKey(name: 'isActive') bool get isActive;@JsonKey(name: 'outstandingDue') double get outstandingDue;
/// Create a copy of CustomerDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$CustomerDtoCopyWith<CustomerDto> get copyWith => _$CustomerDtoCopyWithImpl<CustomerDto>(this as CustomerDto, _$identity);

  /// Serializes this CustomerDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is CustomerDto&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.name, name) || other.name == name)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.address, address) || other.address == address)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.outstandingDue, outstandingDue) || other.outstandingDue == outstandingDue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,customerId,name,phoneNumber,address,isActive,outstandingDue);

@override
String toString() {
  return 'CustomerDto(customerId: $customerId, name: $name, phoneNumber: $phoneNumber, address: $address, isActive: $isActive, outstandingDue: $outstandingDue)';
}


}

/// @nodoc
abstract mixin class $CustomerDtoCopyWith<$Res>  {
  factory $CustomerDtoCopyWith(CustomerDto value, $Res Function(CustomerDto) _then) = _$CustomerDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'customerId') String customerId,@JsonKey(name: 'name') String name,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'address') String? address,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'outstandingDue') double outstandingDue
});




}
/// @nodoc
class _$CustomerDtoCopyWithImpl<$Res>
    implements $CustomerDtoCopyWith<$Res> {
  _$CustomerDtoCopyWithImpl(this._self, this._then);

  final CustomerDto _self;
  final $Res Function(CustomerDto) _then;

/// Create a copy of CustomerDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? customerId = null,Object? name = null,Object? phoneNumber = null,Object? address = freezed,Object? isActive = null,Object? outstandingDue = null,}) {
  return _then(_self.copyWith(
customerId: null == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,address: freezed == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String?,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,outstandingDue: null == outstandingDue ? _self.outstandingDue : outstandingDue // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [CustomerDto].
extension CustomerDtoPatterns on CustomerDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _CustomerDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _CustomerDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _CustomerDto value)  $default,){
final _that = this;
switch (_that) {
case _CustomerDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _CustomerDto value)?  $default,){
final _that = this;
switch (_that) {
case _CustomerDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'customerId')  String customerId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'outstandingDue')  double outstandingDue)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _CustomerDto() when $default != null:
return $default(_that.customerId,_that.name,_that.phoneNumber,_that.address,_that.isActive,_that.outstandingDue);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'customerId')  String customerId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'outstandingDue')  double outstandingDue)  $default,) {final _that = this;
switch (_that) {
case _CustomerDto():
return $default(_that.customerId,_that.name,_that.phoneNumber,_that.address,_that.isActive,_that.outstandingDue);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'customerId')  String customerId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'phoneNumber')  String phoneNumber, @JsonKey(name: 'address')  String? address, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'outstandingDue')  double outstandingDue)?  $default,) {final _that = this;
switch (_that) {
case _CustomerDto() when $default != null:
return $default(_that.customerId,_that.name,_that.phoneNumber,_that.address,_that.isActive,_that.outstandingDue);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _CustomerDto implements CustomerDto {
  const _CustomerDto({@JsonKey(name: 'customerId') required this.customerId, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'phoneNumber') required this.phoneNumber, @JsonKey(name: 'address') this.address, @JsonKey(name: 'isActive') required this.isActive, @JsonKey(name: 'outstandingDue') this.outstandingDue = 0.0});
  factory _CustomerDto.fromJson(Map<String, dynamic> json) => _$CustomerDtoFromJson(json);

@override@JsonKey(name: 'customerId') final  String customerId;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'phoneNumber') final  String phoneNumber;
@override@JsonKey(name: 'address') final  String? address;
@override@JsonKey(name: 'isActive') final  bool isActive;
@override@JsonKey(name: 'outstandingDue') final  double outstandingDue;

/// Create a copy of CustomerDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$CustomerDtoCopyWith<_CustomerDto> get copyWith => __$CustomerDtoCopyWithImpl<_CustomerDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$CustomerDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _CustomerDto&&(identical(other.customerId, customerId) || other.customerId == customerId)&&(identical(other.name, name) || other.name == name)&&(identical(other.phoneNumber, phoneNumber) || other.phoneNumber == phoneNumber)&&(identical(other.address, address) || other.address == address)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.outstandingDue, outstandingDue) || other.outstandingDue == outstandingDue));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,customerId,name,phoneNumber,address,isActive,outstandingDue);

@override
String toString() {
  return 'CustomerDto(customerId: $customerId, name: $name, phoneNumber: $phoneNumber, address: $address, isActive: $isActive, outstandingDue: $outstandingDue)';
}


}

/// @nodoc
abstract mixin class _$CustomerDtoCopyWith<$Res> implements $CustomerDtoCopyWith<$Res> {
  factory _$CustomerDtoCopyWith(_CustomerDto value, $Res Function(_CustomerDto) _then) = __$CustomerDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'customerId') String customerId,@JsonKey(name: 'name') String name,@JsonKey(name: 'phoneNumber') String phoneNumber,@JsonKey(name: 'address') String? address,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'outstandingDue') double outstandingDue
});




}
/// @nodoc
class __$CustomerDtoCopyWithImpl<$Res>
    implements _$CustomerDtoCopyWith<$Res> {
  __$CustomerDtoCopyWithImpl(this._self, this._then);

  final _CustomerDto _self;
  final $Res Function(_CustomerDto) _then;

/// Create a copy of CustomerDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? customerId = null,Object? name = null,Object? phoneNumber = null,Object? address = freezed,Object? isActive = null,Object? outstandingDue = null,}) {
  return _then(_CustomerDto(
customerId: null == customerId ? _self.customerId : customerId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,phoneNumber: null == phoneNumber ? _self.phoneNumber : phoneNumber // ignore: cast_nullable_to_non_nullable
as String,address: freezed == address ? _self.address : address // ignore: cast_nullable_to_non_nullable
as String?,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,outstandingDue: null == outstandingDue ? _self.outstandingDue : outstandingDue // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
