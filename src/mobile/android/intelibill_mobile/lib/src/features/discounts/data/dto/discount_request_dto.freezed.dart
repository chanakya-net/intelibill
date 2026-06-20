// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'discountType') String get discountType;@JsonKey(name: 'discountValue') double get discountValue;@JsonKey(name: 'batchPercentage') double? get batchPercentage;
/// Create a copy of DiscountRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountRequestDtoCopyWith<DiscountRequestDto> get copyWith => _$DiscountRequestDtoCopyWithImpl<DiscountRequestDto>(this as DiscountRequestDto, _$identity);

  /// Serializes this DiscountRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.discountType, discountType) || other.discountType == discountType)&&(identical(other.discountValue, discountValue) || other.discountValue == discountValue)&&(identical(other.batchPercentage, batchPercentage) || other.batchPercentage == batchPercentage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,discountType,discountValue,batchPercentage);

@override
String toString() {
  return 'DiscountRequestDto(name: $name, discountType: $discountType, discountValue: $discountValue, batchPercentage: $batchPercentage)';
}


}

/// @nodoc
abstract mixin class $DiscountRequestDtoCopyWith<$Res>  {
  factory $DiscountRequestDtoCopyWith(DiscountRequestDto value, $Res Function(DiscountRequestDto) _then) = _$DiscountRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'discountType') String discountType,@JsonKey(name: 'discountValue') double discountValue,@JsonKey(name: 'batchPercentage') double? batchPercentage
});




}
/// @nodoc
class _$DiscountRequestDtoCopyWithImpl<$Res>
    implements $DiscountRequestDtoCopyWith<$Res> {
  _$DiscountRequestDtoCopyWithImpl(this._self, this._then);

  final DiscountRequestDto _self;
  final $Res Function(DiscountRequestDto) _then;

/// Create a copy of DiscountRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? discountType = null,Object? discountValue = null,Object? batchPercentage = freezed,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,discountType: null == discountType ? _self.discountType : discountType // ignore: cast_nullable_to_non_nullable
as String,discountValue: null == discountValue ? _self.discountValue : discountValue // ignore: cast_nullable_to_non_nullable
as double,batchPercentage: freezed == batchPercentage ? _self.batchPercentage : batchPercentage // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}

}


/// Adds pattern-matching-related methods to [DiscountRequestDto].
extension DiscountRequestDtoPatterns on DiscountRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'discountType')  String discountType, @JsonKey(name: 'discountValue')  double discountValue, @JsonKey(name: 'batchPercentage')  double? batchPercentage)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DiscountRequestDto() when $default != null:
return $default(_that.name,_that.discountType,_that.discountValue,_that.batchPercentage);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'discountType')  String discountType, @JsonKey(name: 'discountValue')  double discountValue, @JsonKey(name: 'batchPercentage')  double? batchPercentage)  $default,) {final _that = this;
switch (_that) {
case _DiscountRequestDto():
return $default(_that.name,_that.discountType,_that.discountValue,_that.batchPercentage);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'discountType')  String discountType, @JsonKey(name: 'discountValue')  double discountValue, @JsonKey(name: 'batchPercentage')  double? batchPercentage)?  $default,) {final _that = this;
switch (_that) {
case _DiscountRequestDto() when $default != null:
return $default(_that.name,_that.discountType,_that.discountValue,_that.batchPercentage);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountRequestDto implements DiscountRequestDto {
  const _DiscountRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'discountType') required this.discountType, @JsonKey(name: 'discountValue') required this.discountValue, @JsonKey(name: 'batchPercentage') required this.batchPercentage});
  factory _DiscountRequestDto.fromJson(Map<String, dynamic> json) => _$DiscountRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'discountType') final  String discountType;
@override@JsonKey(name: 'discountValue') final  double discountValue;
@override@JsonKey(name: 'batchPercentage') final  double? batchPercentage;

/// Create a copy of DiscountRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountRequestDtoCopyWith<_DiscountRequestDto> get copyWith => __$DiscountRequestDtoCopyWithImpl<_DiscountRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.discountType, discountType) || other.discountType == discountType)&&(identical(other.discountValue, discountValue) || other.discountValue == discountValue)&&(identical(other.batchPercentage, batchPercentage) || other.batchPercentage == batchPercentage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,discountType,discountValue,batchPercentage);

@override
String toString() {
  return 'DiscountRequestDto(name: $name, discountType: $discountType, discountValue: $discountValue, batchPercentage: $batchPercentage)';
}


}

/// @nodoc
abstract mixin class _$DiscountRequestDtoCopyWith<$Res> implements $DiscountRequestDtoCopyWith<$Res> {
  factory _$DiscountRequestDtoCopyWith(_DiscountRequestDto value, $Res Function(_DiscountRequestDto) _then) = __$DiscountRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'discountType') String discountType,@JsonKey(name: 'discountValue') double discountValue,@JsonKey(name: 'batchPercentage') double? batchPercentage
});




}
/// @nodoc
class __$DiscountRequestDtoCopyWithImpl<$Res>
    implements _$DiscountRequestDtoCopyWith<$Res> {
  __$DiscountRequestDtoCopyWithImpl(this._self, this._then);

  final _DiscountRequestDto _self;
  final $Res Function(_DiscountRequestDto) _then;

/// Create a copy of DiscountRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? discountType = null,Object? discountValue = null,Object? batchPercentage = freezed,}) {
  return _then(_DiscountRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,discountType: null == discountType ? _self.discountType : discountType // ignore: cast_nullable_to_non_nullable
as String,discountValue: null == discountValue ? _self.discountValue : discountValue // ignore: cast_nullable_to_non_nullable
as double,batchPercentage: freezed == batchPercentage ? _self.batchPercentage : batchPercentage // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}


}

// dart format on
