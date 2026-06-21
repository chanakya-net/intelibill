// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'update_service_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$UpdateServiceRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'description') String? get description;@JsonKey(name: 'price') double get price;@JsonKey(name: 'hsnCode') String? get hsnCode;@JsonKey(name: 'taxRatePercent') double get taxRatePercent;@JsonKey(name: 'taxIncluded') bool get taxIncluded;
/// Create a copy of UpdateServiceRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$UpdateServiceRequestDtoCopyWith<UpdateServiceRequestDto> get copyWith => _$UpdateServiceRequestDtoCopyWithImpl<UpdateServiceRequestDto>(this as UpdateServiceRequestDto, _$identity);

  /// Serializes this UpdateServiceRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is UpdateServiceRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.price, price) || other.price == price)&&(identical(other.hsnCode, hsnCode) || other.hsnCode == hsnCode)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,description,price,hsnCode,taxRatePercent,taxIncluded);

@override
String toString() {
  return 'UpdateServiceRequestDto(name: $name, description: $description, price: $price, hsnCode: $hsnCode, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded)';
}


}

/// @nodoc
abstract mixin class $UpdateServiceRequestDtoCopyWith<$Res>  {
  factory $UpdateServiceRequestDtoCopyWith(UpdateServiceRequestDto value, $Res Function(UpdateServiceRequestDto) _then) = _$UpdateServiceRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'description') String? description,@JsonKey(name: 'price') double price,@JsonKey(name: 'hsnCode') String? hsnCode,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded
});




}
/// @nodoc
class _$UpdateServiceRequestDtoCopyWithImpl<$Res>
    implements $UpdateServiceRequestDtoCopyWith<$Res> {
  _$UpdateServiceRequestDtoCopyWithImpl(this._self, this._then);

  final UpdateServiceRequestDto _self;
  final $Res Function(UpdateServiceRequestDto) _then;

/// Create a copy of UpdateServiceRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? description = freezed,Object? price = null,Object? hsnCode = freezed,Object? taxRatePercent = null,Object? taxIncluded = null,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,price: null == price ? _self.price : price // ignore: cast_nullable_to_non_nullable
as double,hsnCode: freezed == hsnCode ? _self.hsnCode : hsnCode // ignore: cast_nullable_to_non_nullable
as String?,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [UpdateServiceRequestDto].
extension UpdateServiceRequestDtoPatterns on UpdateServiceRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _UpdateServiceRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _UpdateServiceRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _UpdateServiceRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _UpdateServiceRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _UpdateServiceRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _UpdateServiceRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'price')  double price, @JsonKey(name: 'hsnCode')  String? hsnCode, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _UpdateServiceRequestDto() when $default != null:
return $default(_that.name,_that.description,_that.price,_that.hsnCode,_that.taxRatePercent,_that.taxIncluded);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'price')  double price, @JsonKey(name: 'hsnCode')  String? hsnCode, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded)  $default,) {final _that = this;
switch (_that) {
case _UpdateServiceRequestDto():
return $default(_that.name,_that.description,_that.price,_that.hsnCode,_that.taxRatePercent,_that.taxIncluded);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'price')  double price, @JsonKey(name: 'hsnCode')  String? hsnCode, @JsonKey(name: 'taxRatePercent')  double taxRatePercent, @JsonKey(name: 'taxIncluded')  bool taxIncluded)?  $default,) {final _that = this;
switch (_that) {
case _UpdateServiceRequestDto() when $default != null:
return $default(_that.name,_that.description,_that.price,_that.hsnCode,_that.taxRatePercent,_that.taxIncluded);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _UpdateServiceRequestDto implements UpdateServiceRequestDto {
  const _UpdateServiceRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'description') this.description, @JsonKey(name: 'price') required this.price, @JsonKey(name: 'hsnCode') this.hsnCode, @JsonKey(name: 'taxRatePercent') required this.taxRatePercent, @JsonKey(name: 'taxIncluded') required this.taxIncluded});
  factory _UpdateServiceRequestDto.fromJson(Map<String, dynamic> json) => _$UpdateServiceRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'description') final  String? description;
@override@JsonKey(name: 'price') final  double price;
@override@JsonKey(name: 'hsnCode') final  String? hsnCode;
@override@JsonKey(name: 'taxRatePercent') final  double taxRatePercent;
@override@JsonKey(name: 'taxIncluded') final  bool taxIncluded;

/// Create a copy of UpdateServiceRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$UpdateServiceRequestDtoCopyWith<_UpdateServiceRequestDto> get copyWith => __$UpdateServiceRequestDtoCopyWithImpl<_UpdateServiceRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$UpdateServiceRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _UpdateServiceRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.price, price) || other.price == price)&&(identical(other.hsnCode, hsnCode) || other.hsnCode == hsnCode)&&(identical(other.taxRatePercent, taxRatePercent) || other.taxRatePercent == taxRatePercent)&&(identical(other.taxIncluded, taxIncluded) || other.taxIncluded == taxIncluded));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,description,price,hsnCode,taxRatePercent,taxIncluded);

@override
String toString() {
  return 'UpdateServiceRequestDto(name: $name, description: $description, price: $price, hsnCode: $hsnCode, taxRatePercent: $taxRatePercent, taxIncluded: $taxIncluded)';
}


}

/// @nodoc
abstract mixin class _$UpdateServiceRequestDtoCopyWith<$Res> implements $UpdateServiceRequestDtoCopyWith<$Res> {
  factory _$UpdateServiceRequestDtoCopyWith(_UpdateServiceRequestDto value, $Res Function(_UpdateServiceRequestDto) _then) = __$UpdateServiceRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'description') String? description,@JsonKey(name: 'price') double price,@JsonKey(name: 'hsnCode') String? hsnCode,@JsonKey(name: 'taxRatePercent') double taxRatePercent,@JsonKey(name: 'taxIncluded') bool taxIncluded
});




}
/// @nodoc
class __$UpdateServiceRequestDtoCopyWithImpl<$Res>
    implements _$UpdateServiceRequestDtoCopyWith<$Res> {
  __$UpdateServiceRequestDtoCopyWithImpl(this._self, this._then);

  final _UpdateServiceRequestDto _self;
  final $Res Function(_UpdateServiceRequestDto) _then;

/// Create a copy of UpdateServiceRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? description = freezed,Object? price = null,Object? hsnCode = freezed,Object? taxRatePercent = null,Object? taxIncluded = null,}) {
  return _then(_UpdateServiceRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,price: null == price ? _self.price : price // ignore: cast_nullable_to_non_nullable
as double,hsnCode: freezed == hsnCode ? _self.hsnCode : hsnCode // ignore: cast_nullable_to_non_nullable
as String?,taxRatePercent: null == taxRatePercent ? _self.taxRatePercent : taxRatePercent // ignore: cast_nullable_to_non_nullable
as double,taxIncluded: null == taxIncluded ? _self.taxIncluded : taxIncluded // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}

// dart format on
