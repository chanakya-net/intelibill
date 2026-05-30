// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'update_item_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$UpdateItemRequestDto {

@JsonKey(name: 'name') String get name;@JsonKey(name: 'barcode') String get barcode;@JsonKey(name: 'description') String? get description;@JsonKey(name: 'uom') String get uom;@JsonKey(name: 'isActive') bool get isActive;
/// Create a copy of UpdateItemRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$UpdateItemRequestDtoCopyWith<UpdateItemRequestDto> get copyWith => _$UpdateItemRequestDtoCopyWithImpl<UpdateItemRequestDto>(this as UpdateItemRequestDto, _$identity);

  /// Serializes this UpdateItemRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is UpdateItemRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.description, description) || other.description == description)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.isActive, isActive) || other.isActive == isActive));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,barcode,description,uom,isActive);

@override
String toString() {
  return 'UpdateItemRequestDto(name: $name, barcode: $barcode, description: $description, uom: $uom, isActive: $isActive)';
}


}

/// @nodoc
abstract mixin class $UpdateItemRequestDtoCopyWith<$Res>  {
  factory $UpdateItemRequestDtoCopyWith(UpdateItemRequestDto value, $Res Function(UpdateItemRequestDto) _then) = _$UpdateItemRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'description') String? description,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'isActive') bool isActive
});




}
/// @nodoc
class _$UpdateItemRequestDtoCopyWithImpl<$Res>
    implements $UpdateItemRequestDtoCopyWith<$Res> {
  _$UpdateItemRequestDtoCopyWithImpl(this._self, this._then);

  final UpdateItemRequestDto _self;
  final $Res Function(UpdateItemRequestDto) _then;

/// Create a copy of UpdateItemRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? name = null,Object? barcode = null,Object? description = freezed,Object? uom = null,Object? isActive = null,}) {
  return _then(_self.copyWith(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [UpdateItemRequestDto].
extension UpdateItemRequestDtoPatterns on UpdateItemRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _UpdateItemRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _UpdateItemRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _UpdateItemRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _UpdateItemRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _UpdateItemRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _UpdateItemRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'isActive')  bool isActive)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _UpdateItemRequestDto() when $default != null:
return $default(_that.name,_that.barcode,_that.description,_that.uom,_that.isActive);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'isActive')  bool isActive)  $default,) {final _that = this;
switch (_that) {
case _UpdateItemRequestDto():
return $default(_that.name,_that.barcode,_that.description,_that.uom,_that.isActive);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'name')  String name, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'isActive')  bool isActive)?  $default,) {final _that = this;
switch (_that) {
case _UpdateItemRequestDto() when $default != null:
return $default(_that.name,_that.barcode,_that.description,_that.uom,_that.isActive);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _UpdateItemRequestDto implements UpdateItemRequestDto {
  const _UpdateItemRequestDto({@JsonKey(name: 'name') required this.name, @JsonKey(name: 'barcode') required this.barcode, @JsonKey(name: 'description') this.description, @JsonKey(name: 'uom') required this.uom, @JsonKey(name: 'isActive') required this.isActive});
  factory _UpdateItemRequestDto.fromJson(Map<String, dynamic> json) => _$UpdateItemRequestDtoFromJson(json);

@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'barcode') final  String barcode;
@override@JsonKey(name: 'description') final  String? description;
@override@JsonKey(name: 'uom') final  String uom;
@override@JsonKey(name: 'isActive') final  bool isActive;

/// Create a copy of UpdateItemRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$UpdateItemRequestDtoCopyWith<_UpdateItemRequestDto> get copyWith => __$UpdateItemRequestDtoCopyWithImpl<_UpdateItemRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$UpdateItemRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _UpdateItemRequestDto&&(identical(other.name, name) || other.name == name)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.description, description) || other.description == description)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.isActive, isActive) || other.isActive == isActive));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,name,barcode,description,uom,isActive);

@override
String toString() {
  return 'UpdateItemRequestDto(name: $name, barcode: $barcode, description: $description, uom: $uom, isActive: $isActive)';
}


}

/// @nodoc
abstract mixin class _$UpdateItemRequestDtoCopyWith<$Res> implements $UpdateItemRequestDtoCopyWith<$Res> {
  factory _$UpdateItemRequestDtoCopyWith(_UpdateItemRequestDto value, $Res Function(_UpdateItemRequestDto) _then) = __$UpdateItemRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'name') String name,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'description') String? description,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'isActive') bool isActive
});




}
/// @nodoc
class __$UpdateItemRequestDtoCopyWithImpl<$Res>
    implements _$UpdateItemRequestDtoCopyWith<$Res> {
  __$UpdateItemRequestDtoCopyWithImpl(this._self, this._then);

  final _UpdateItemRequestDto _self;
  final $Res Function(_UpdateItemRequestDto) _then;

/// Create a copy of UpdateItemRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? name = null,Object? barcode = null,Object? description = freezed,Object? uom = null,Object? isActive = null,}) {
  return _then(_UpdateItemRequestDto(
name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}

// dart format on
