// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'generate_item_barcode_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$GenerateItemBarcodeResponseDto {

@JsonKey(name: 'barcode') String get barcode;
/// Create a copy of GenerateItemBarcodeResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$GenerateItemBarcodeResponseDtoCopyWith<GenerateItemBarcodeResponseDto> get copyWith => _$GenerateItemBarcodeResponseDtoCopyWithImpl<GenerateItemBarcodeResponseDto>(this as GenerateItemBarcodeResponseDto, _$identity);

  /// Serializes this GenerateItemBarcodeResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is GenerateItemBarcodeResponseDto&&(identical(other.barcode, barcode) || other.barcode == barcode));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,barcode);

@override
String toString() {
  return 'GenerateItemBarcodeResponseDto(barcode: $barcode)';
}


}

/// @nodoc
abstract mixin class $GenerateItemBarcodeResponseDtoCopyWith<$Res>  {
  factory $GenerateItemBarcodeResponseDtoCopyWith(GenerateItemBarcodeResponseDto value, $Res Function(GenerateItemBarcodeResponseDto) _then) = _$GenerateItemBarcodeResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'barcode') String barcode
});




}
/// @nodoc
class _$GenerateItemBarcodeResponseDtoCopyWithImpl<$Res>
    implements $GenerateItemBarcodeResponseDtoCopyWith<$Res> {
  _$GenerateItemBarcodeResponseDtoCopyWithImpl(this._self, this._then);

  final GenerateItemBarcodeResponseDto _self;
  final $Res Function(GenerateItemBarcodeResponseDto) _then;

/// Create a copy of GenerateItemBarcodeResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? barcode = null,}) {
  return _then(_self.copyWith(
barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [GenerateItemBarcodeResponseDto].
extension GenerateItemBarcodeResponseDtoPatterns on GenerateItemBarcodeResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _GenerateItemBarcodeResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _GenerateItemBarcodeResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _GenerateItemBarcodeResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _GenerateItemBarcodeResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _GenerateItemBarcodeResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _GenerateItemBarcodeResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'barcode')  String barcode)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _GenerateItemBarcodeResponseDto() when $default != null:
return $default(_that.barcode);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'barcode')  String barcode)  $default,) {final _that = this;
switch (_that) {
case _GenerateItemBarcodeResponseDto():
return $default(_that.barcode);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'barcode')  String barcode)?  $default,) {final _that = this;
switch (_that) {
case _GenerateItemBarcodeResponseDto() when $default != null:
return $default(_that.barcode);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _GenerateItemBarcodeResponseDto implements GenerateItemBarcodeResponseDto {
  const _GenerateItemBarcodeResponseDto({@JsonKey(name: 'barcode') required this.barcode});
  factory _GenerateItemBarcodeResponseDto.fromJson(Map<String, dynamic> json) => _$GenerateItemBarcodeResponseDtoFromJson(json);

@override@JsonKey(name: 'barcode') final  String barcode;

/// Create a copy of GenerateItemBarcodeResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$GenerateItemBarcodeResponseDtoCopyWith<_GenerateItemBarcodeResponseDto> get copyWith => __$GenerateItemBarcodeResponseDtoCopyWithImpl<_GenerateItemBarcodeResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$GenerateItemBarcodeResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _GenerateItemBarcodeResponseDto&&(identical(other.barcode, barcode) || other.barcode == barcode));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,barcode);

@override
String toString() {
  return 'GenerateItemBarcodeResponseDto(barcode: $barcode)';
}


}

/// @nodoc
abstract mixin class _$GenerateItemBarcodeResponseDtoCopyWith<$Res> implements $GenerateItemBarcodeResponseDtoCopyWith<$Res> {
  factory _$GenerateItemBarcodeResponseDtoCopyWith(_GenerateItemBarcodeResponseDto value, $Res Function(_GenerateItemBarcodeResponseDto) _then) = __$GenerateItemBarcodeResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'barcode') String barcode
});




}
/// @nodoc
class __$GenerateItemBarcodeResponseDtoCopyWithImpl<$Res>
    implements _$GenerateItemBarcodeResponseDtoCopyWith<$Res> {
  __$GenerateItemBarcodeResponseDtoCopyWithImpl(this._self, this._then);

  final _GenerateItemBarcodeResponseDto _self;
  final $Res Function(_GenerateItemBarcodeResponseDto) _then;

/// Create a copy of GenerateItemBarcodeResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? barcode = null,}) {
  return _then(_GenerateItemBarcodeResponseDto(
barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
