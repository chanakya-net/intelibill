// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_preview_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountPreviewResponseDto {

@JsonKey(name: 'totalCostReduction') double get totalCostReduction;@JsonKey(name: 'error') String? get error;@JsonKey(name: 'estimatedProfit') double get estimatedProfit;
/// Create a copy of DiscountPreviewResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountPreviewResponseDtoCopyWith<DiscountPreviewResponseDto> get copyWith => _$DiscountPreviewResponseDtoCopyWithImpl<DiscountPreviewResponseDto>(this as DiscountPreviewResponseDto, _$identity);

  /// Serializes this DiscountPreviewResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountPreviewResponseDto&&(identical(other.totalCostReduction, totalCostReduction) || other.totalCostReduction == totalCostReduction)&&(identical(other.error, error) || other.error == error)&&(identical(other.estimatedProfit, estimatedProfit) || other.estimatedProfit == estimatedProfit));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,totalCostReduction,error,estimatedProfit);

@override
String toString() {
  return 'DiscountPreviewResponseDto(totalCostReduction: $totalCostReduction, error: $error, estimatedProfit: $estimatedProfit)';
}


}

/// @nodoc
abstract mixin class $DiscountPreviewResponseDtoCopyWith<$Res>  {
  factory $DiscountPreviewResponseDtoCopyWith(DiscountPreviewResponseDto value, $Res Function(DiscountPreviewResponseDto) _then) = _$DiscountPreviewResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'totalCostReduction') double totalCostReduction,@JsonKey(name: 'error') String? error,@JsonKey(name: 'estimatedProfit') double estimatedProfit
});




}
/// @nodoc
class _$DiscountPreviewResponseDtoCopyWithImpl<$Res>
    implements $DiscountPreviewResponseDtoCopyWith<$Res> {
  _$DiscountPreviewResponseDtoCopyWithImpl(this._self, this._then);

  final DiscountPreviewResponseDto _self;
  final $Res Function(DiscountPreviewResponseDto) _then;

/// Create a copy of DiscountPreviewResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? totalCostReduction = null,Object? error = freezed,Object? estimatedProfit = null,}) {
  return _then(_self.copyWith(
totalCostReduction: null == totalCostReduction ? _self.totalCostReduction : totalCostReduction // ignore: cast_nullable_to_non_nullable
as double,error: freezed == error ? _self.error : error // ignore: cast_nullable_to_non_nullable
as String?,estimatedProfit: null == estimatedProfit ? _self.estimatedProfit : estimatedProfit // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [DiscountPreviewResponseDto].
extension DiscountPreviewResponseDtoPatterns on DiscountPreviewResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountPreviewResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountPreviewResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountPreviewResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountPreviewResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountPreviewResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountPreviewResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'totalCostReduction')  double totalCostReduction, @JsonKey(name: 'error')  String? error, @JsonKey(name: 'estimatedProfit')  double estimatedProfit)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DiscountPreviewResponseDto() when $default != null:
return $default(_that.totalCostReduction,_that.error,_that.estimatedProfit);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'totalCostReduction')  double totalCostReduction, @JsonKey(name: 'error')  String? error, @JsonKey(name: 'estimatedProfit')  double estimatedProfit)  $default,) {final _that = this;
switch (_that) {
case _DiscountPreviewResponseDto():
return $default(_that.totalCostReduction,_that.error,_that.estimatedProfit);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'totalCostReduction')  double totalCostReduction, @JsonKey(name: 'error')  String? error, @JsonKey(name: 'estimatedProfit')  double estimatedProfit)?  $default,) {final _that = this;
switch (_that) {
case _DiscountPreviewResponseDto() when $default != null:
return $default(_that.totalCostReduction,_that.error,_that.estimatedProfit);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountPreviewResponseDto extends DiscountPreviewResponseDto {
  const _DiscountPreviewResponseDto({@JsonKey(name: 'totalCostReduction') required this.totalCostReduction, @JsonKey(name: 'error') required this.error, @JsonKey(name: 'estimatedProfit') required this.estimatedProfit}): super._();
  factory _DiscountPreviewResponseDto.fromJson(Map<String, dynamic> json) => _$DiscountPreviewResponseDtoFromJson(json);

@override@JsonKey(name: 'totalCostReduction') final  double totalCostReduction;
@override@JsonKey(name: 'error') final  String? error;
@override@JsonKey(name: 'estimatedProfit') final  double estimatedProfit;

/// Create a copy of DiscountPreviewResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountPreviewResponseDtoCopyWith<_DiscountPreviewResponseDto> get copyWith => __$DiscountPreviewResponseDtoCopyWithImpl<_DiscountPreviewResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountPreviewResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountPreviewResponseDto&&(identical(other.totalCostReduction, totalCostReduction) || other.totalCostReduction == totalCostReduction)&&(identical(other.error, error) || other.error == error)&&(identical(other.estimatedProfit, estimatedProfit) || other.estimatedProfit == estimatedProfit));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,totalCostReduction,error,estimatedProfit);

@override
String toString() {
  return 'DiscountPreviewResponseDto(totalCostReduction: $totalCostReduction, error: $error, estimatedProfit: $estimatedProfit)';
}


}

/// @nodoc
abstract mixin class _$DiscountPreviewResponseDtoCopyWith<$Res> implements $DiscountPreviewResponseDtoCopyWith<$Res> {
  factory _$DiscountPreviewResponseDtoCopyWith(_DiscountPreviewResponseDto value, $Res Function(_DiscountPreviewResponseDto) _then) = __$DiscountPreviewResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'totalCostReduction') double totalCostReduction,@JsonKey(name: 'error') String? error,@JsonKey(name: 'estimatedProfit') double estimatedProfit
});




}
/// @nodoc
class __$DiscountPreviewResponseDtoCopyWithImpl<$Res>
    implements _$DiscountPreviewResponseDtoCopyWith<$Res> {
  __$DiscountPreviewResponseDtoCopyWithImpl(this._self, this._then);

  final _DiscountPreviewResponseDto _self;
  final $Res Function(_DiscountPreviewResponseDto) _then;

/// Create a copy of DiscountPreviewResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? totalCostReduction = null,Object? error = freezed,Object? estimatedProfit = null,}) {
  return _then(_DiscountPreviewResponseDto(
totalCostReduction: null == totalCostReduction ? _self.totalCostReduction : totalCostReduction // ignore: cast_nullable_to_non_nullable
as double,error: freezed == error ? _self.error : error // ignore: cast_nullable_to_non_nullable
as String?,estimatedProfit: null == estimatedProfit ? _self.estimatedProfit : estimatedProfit // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
