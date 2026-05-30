// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'item_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ItemDto {

@JsonKey(name: 'id') String get id;@JsonKey(name: 'name') String get name;@JsonKey(name: 'barcode') String get barcode;@JsonKey(name: 'description') String? get description;@JsonKey(name: 'uom') String get uom;@JsonKey(name: 'isActive') bool get isActive;@JsonKey(name: 'currentStock') double get currentStock;
/// Create a copy of ItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ItemDtoCopyWith<ItemDto> get copyWith => _$ItemDtoCopyWithImpl<ItemDto>(this as ItemDto, _$identity);

  /// Serializes this ItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ItemDto&&(identical(other.id, id) || other.id == id)&&(identical(other.name, name) || other.name == name)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.description, description) || other.description == description)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.currentStock, currentStock) || other.currentStock == currentStock));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,name,barcode,description,uom,isActive,currentStock);

@override
String toString() {
  return 'ItemDto(id: $id, name: $name, barcode: $barcode, description: $description, uom: $uom, isActive: $isActive, currentStock: $currentStock)';
}


}

/// @nodoc
abstract mixin class $ItemDtoCopyWith<$Res>  {
  factory $ItemDtoCopyWith(ItemDto value, $Res Function(ItemDto) _then) = _$ItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'name') String name,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'description') String? description,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'currentStock') double currentStock
});




}
/// @nodoc
class _$ItemDtoCopyWithImpl<$Res>
    implements $ItemDtoCopyWith<$Res> {
  _$ItemDtoCopyWithImpl(this._self, this._then);

  final ItemDto _self;
  final $Res Function(ItemDto) _then;

/// Create a copy of ItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? name = null,Object? barcode = null,Object? description = freezed,Object? uom = null,Object? isActive = null,Object? currentStock = null,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,currentStock: null == currentStock ? _self.currentStock : currentStock // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [ItemDto].
extension ItemDtoPatterns on ItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ItemDto value)  $default,){
final _that = this;
switch (_that) {
case _ItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _ItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'name')  String name, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'currentStock')  double currentStock)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ItemDto() when $default != null:
return $default(_that.id,_that.name,_that.barcode,_that.description,_that.uom,_that.isActive,_that.currentStock);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'name')  String name, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'currentStock')  double currentStock)  $default,) {final _that = this;
switch (_that) {
case _ItemDto():
return $default(_that.id,_that.name,_that.barcode,_that.description,_that.uom,_that.isActive,_that.currentStock);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'name')  String name, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'uom')  String uom, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'currentStock')  double currentStock)?  $default,) {final _that = this;
switch (_that) {
case _ItemDto() when $default != null:
return $default(_that.id,_that.name,_that.barcode,_that.description,_that.uom,_that.isActive,_that.currentStock);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ItemDto implements ItemDto {
  const _ItemDto({@JsonKey(name: 'id') required this.id, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'barcode') required this.barcode, @JsonKey(name: 'description') this.description, @JsonKey(name: 'uom') required this.uom, @JsonKey(name: 'isActive') required this.isActive, @JsonKey(name: 'currentStock') this.currentStock = 0.0});
  factory _ItemDto.fromJson(Map<String, dynamic> json) => _$ItemDtoFromJson(json);

@override@JsonKey(name: 'id') final  String id;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'barcode') final  String barcode;
@override@JsonKey(name: 'description') final  String? description;
@override@JsonKey(name: 'uom') final  String uom;
@override@JsonKey(name: 'isActive') final  bool isActive;
@override@JsonKey(name: 'currentStock') final  double currentStock;

/// Create a copy of ItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ItemDtoCopyWith<_ItemDto> get copyWith => __$ItemDtoCopyWithImpl<_ItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ItemDto&&(identical(other.id, id) || other.id == id)&&(identical(other.name, name) || other.name == name)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.description, description) || other.description == description)&&(identical(other.uom, uom) || other.uom == uom)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.currentStock, currentStock) || other.currentStock == currentStock));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,name,barcode,description,uom,isActive,currentStock);

@override
String toString() {
  return 'ItemDto(id: $id, name: $name, barcode: $barcode, description: $description, uom: $uom, isActive: $isActive, currentStock: $currentStock)';
}


}

/// @nodoc
abstract mixin class _$ItemDtoCopyWith<$Res> implements $ItemDtoCopyWith<$Res> {
  factory _$ItemDtoCopyWith(_ItemDto value, $Res Function(_ItemDto) _then) = __$ItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'name') String name,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'description') String? description,@JsonKey(name: 'uom') String uom,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'currentStock') double currentStock
});




}
/// @nodoc
class __$ItemDtoCopyWithImpl<$Res>
    implements _$ItemDtoCopyWith<$Res> {
  __$ItemDtoCopyWithImpl(this._self, this._then);

  final _ItemDto _self;
  final $Res Function(_ItemDto) _then;

/// Create a copy of ItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? name = null,Object? barcode = null,Object? description = freezed,Object? uom = null,Object? isActive = null,Object? currentStock = null,}) {
  return _then(_ItemDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,uom: null == uom ? _self.uom : uom // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,currentStock: null == currentStock ? _self.currentStock : currentStock // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}

// dart format on
