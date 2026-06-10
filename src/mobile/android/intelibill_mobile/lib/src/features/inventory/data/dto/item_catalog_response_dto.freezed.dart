// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'item_catalog_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ItemCatalogResponseDto {

@JsonKey(name: 'items') List<ItemDto> get items;@JsonKey(name: 'totalCount') int get totalCount;@JsonKey(name: 'pageNumber') int get pageNumber;@JsonKey(name: 'pageSize') int get pageSize;
/// Create a copy of ItemCatalogResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ItemCatalogResponseDtoCopyWith<ItemCatalogResponseDto> get copyWith => _$ItemCatalogResponseDtoCopyWithImpl<ItemCatalogResponseDto>(this as ItemCatalogResponseDto, _$identity);

  /// Serializes this ItemCatalogResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ItemCatalogResponseDto&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'ItemCatalogResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class $ItemCatalogResponseDtoCopyWith<$Res>  {
  factory $ItemCatalogResponseDtoCopyWith(ItemCatalogResponseDto value, $Res Function(ItemCatalogResponseDto) _then) = _$ItemCatalogResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'items') List<ItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize
});




}
/// @nodoc
class _$ItemCatalogResponseDtoCopyWithImpl<$Res>
    implements $ItemCatalogResponseDtoCopyWith<$Res> {
  _$ItemCatalogResponseDtoCopyWithImpl(this._self, this._then);

  final ItemCatalogResponseDto _self;
  final $Res Function(ItemCatalogResponseDto) _then;

/// Create a copy of ItemCatalogResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<ItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}

}


/// Adds pattern-matching-related methods to [ItemCatalogResponseDto].
extension ItemCatalogResponseDtoPatterns on ItemCatalogResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ItemCatalogResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ItemCatalogResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ItemCatalogResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _ItemCatalogResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ItemCatalogResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _ItemCatalogResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<ItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ItemCatalogResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<ItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)  $default,) {final _that = this;
switch (_that) {
case _ItemCatalogResponseDto():
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'items')  List<ItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)?  $default,) {final _that = this;
switch (_that) {
case _ItemCatalogResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ItemCatalogResponseDto implements ItemCatalogResponseDto {
  const _ItemCatalogResponseDto({@JsonKey(name: 'items') final  List<ItemDto> items = const [], @JsonKey(name: 'totalCount') required this.totalCount, @JsonKey(name: 'pageNumber') required this.pageNumber, @JsonKey(name: 'pageSize') required this.pageSize}): _items = items;
  factory _ItemCatalogResponseDto.fromJson(Map<String, dynamic> json) => _$ItemCatalogResponseDtoFromJson(json);

 final  List<ItemDto> _items;
@override@JsonKey(name: 'items') List<ItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override@JsonKey(name: 'totalCount') final  int totalCount;
@override@JsonKey(name: 'pageNumber') final  int pageNumber;
@override@JsonKey(name: 'pageSize') final  int pageSize;

/// Create a copy of ItemCatalogResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ItemCatalogResponseDtoCopyWith<_ItemCatalogResponseDto> get copyWith => __$ItemCatalogResponseDtoCopyWithImpl<_ItemCatalogResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ItemCatalogResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ItemCatalogResponseDto&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'ItemCatalogResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class _$ItemCatalogResponseDtoCopyWith<$Res> implements $ItemCatalogResponseDtoCopyWith<$Res> {
  factory _$ItemCatalogResponseDtoCopyWith(_ItemCatalogResponseDto value, $Res Function(_ItemCatalogResponseDto) _then) = __$ItemCatalogResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'items') List<ItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize
});




}
/// @nodoc
class __$ItemCatalogResponseDtoCopyWithImpl<$Res>
    implements _$ItemCatalogResponseDtoCopyWith<$Res> {
  __$ItemCatalogResponseDtoCopyWithImpl(this._self, this._then);

  final _ItemCatalogResponseDto _self;
  final $Res Function(_ItemCatalogResponseDto) _then;

/// Create a copy of ItemCatalogResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_ItemCatalogResponseDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<ItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}


}

// dart format on
