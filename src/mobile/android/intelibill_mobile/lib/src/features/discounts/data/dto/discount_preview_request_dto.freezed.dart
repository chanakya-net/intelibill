// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_preview_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountPreviewRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'discountType') String get discountType;@JsonKey(name: 'discountValue') double get discountValue;@JsonKey(name: 'batchPercentage') double? get batchPercentage;
/// Create a copy of DiscountPreviewRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountPreviewRequestDtoCopyWith<DiscountPreviewRequestDto> get copyWith => _$DiscountPreviewRequestDtoCopyWithImpl<DiscountPreviewRequestDto>(this as DiscountPreviewRequestDto, _$identity);

  /// Serializes this DiscountPreviewRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountPreviewRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.discountType, discountType) || other.discountType == discountType)&&(identical(other.discountValue, discountValue) || other.discountValue == discountValue)&&(identical(other.batchPercentage, batchPercentage) || other.batchPercentage == batchPercentage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,discountType,discountValue,batchPercentage);

@override
String toString() {
  return 'DiscountPreviewRequestDto(name: $name, discountType: $discountType, discountValue: $discountValue, batchPercentage: $batchPercentage)';
}


}

/// @nodoc
abstract mixin class $DiscountPreviewRequestDtoCopyWith<$Res>  {
  factory $DiscountPreviewRequestDtoCopyWith(DiscountPreviewRequestDto value, $Res Function(DiscountPreviewRequestDto) _then) = _$DiscountPreviewRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'discountType') String discountType,@JsonKey(name: 'discountValue') double discountValue,@JsonKey(name: 'batchPercentage') double? batchPercentage
});




}
/// @nodoc
class _$DiscountPreviewRequestDtoCopyWithImpl<$Res>
    implements $DiscountPreviewRequestDtoCopyWith<$Res> {
  _$DiscountPreviewRequestDtoCopyWithImpl(this._self, this._then);

  final DiscountPreviewRequestDto _self;
  final $Res Function(DiscountPreviewRequestDto) _then;

/// Create a copy of DiscountPreviewRequestDto
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


/// Adds pattern-matching-related methods to [DiscountPreviewRequestDto].
extension DiscountPreviewRequestDtoPatterns on DiscountPreviewRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountPreviewRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountPreviewRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountPreviewRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountPreviewRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountPreviewRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountPreviewRequestDto() when $default != null:
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
case _DiscountPreviewRequestDto() when $default != null:
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
case _DiscountPreviewRequestDto():
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
case _DiscountPreviewRequestDto() when $default != null:
return $default(_that.name,_that.discountType,_that.discountValue,_that.batchPercentage);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountPreviewRequestDto implements DiscountPreviewRequestDto {
  const _DiscountPreviewRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'discountType') required this.discountType, @JsonKey(name: 'discountValue') required this.discountValue, @JsonKey(name: 'batchPercentage') required this.batchPercentage});
  factory _DiscountPreviewRequestDto.fromJson(Map<String, dynamic> json) => _$DiscountPreviewRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'discountType') final  String discountType;
@override@JsonKey(name: 'discountValue') final  double discountValue;
@override@JsonKey(name: 'batchPercentage') final  double? batchPercentage;

/// Create a copy of DiscountPreviewRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountPreviewRequestDtoCopyWith<_DiscountPreviewRequestDto> get copyWith => __$DiscountPreviewRequestDtoCopyWithImpl<_DiscountPreviewRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountPreviewRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountPreviewRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.discountType, discountType) || other.discountType == discountType)&&(identical(other.discountValue, discountValue) || other.discountValue == discountValue)&&(identical(other.batchPercentage, batchPercentage) || other.batchPercentage == batchPercentage));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,discountType,discountValue,batchPercentage);

@override
String toString() {
  return 'DiscountPreviewRequestDto(name: $name, discountType: $discountType, discountValue: $discountValue, batchPercentage: $batchPercentage)';
}


}

/// @nodoc
abstract mixin class _$DiscountPreviewRequestDtoCopyWith<$Res> implements $DiscountPreviewRequestDtoCopyWith<$Res> {
  factory _$DiscountPreviewRequestDtoCopyWith(_DiscountPreviewRequestDto value, $Res Function(_DiscountPreviewRequestDto) _then) = __$DiscountPreviewRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'discountType') String discountType,@JsonKey(name: 'discountValue') double discountValue,@JsonKey(name: 'batchPercentage') double? batchPercentage
});




}
/// @nodoc
class __$DiscountPreviewRequestDtoCopyWithImpl<$Res>
    implements _$DiscountPreviewRequestDtoCopyWith<$Res> {
  __$DiscountPreviewRequestDtoCopyWithImpl(this._self, this._then);

  final _DiscountPreviewRequestDto _self;
  final $Res Function(_DiscountPreviewRequestDto) _then;

/// Create a copy of DiscountPreviewRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? discountType = null,Object? discountValue = null,Object? batchPercentage = freezed,}) {
  return _then(_DiscountPreviewRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,discountType: null == discountType ? _self.discountType : discountType // ignore: cast_nullable_to_non_nullable
as String,discountValue: null == discountValue ? _self.discountValue : discountValue // ignore: cast_nullable_to_non_nullable
as double,batchPercentage: freezed == batchPercentage ? _self.batchPercentage : batchPercentage // ignore: cast_nullable_to_non_nullable
as double?,
  ));
}


}

// dart format on
