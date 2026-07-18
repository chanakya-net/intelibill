// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'purchase_order_page_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$PurchaseOrderPageDto {

 List<PurchaseOrderListItemDto> get items; int get totalCount; int get pageNumber; int get pageSize;
/// Create a copy of PurchaseOrderPageDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$PurchaseOrderPageDtoCopyWith<PurchaseOrderPageDto> get copyWith => _$PurchaseOrderPageDtoCopyWithImpl<PurchaseOrderPageDto>(this as PurchaseOrderPageDto, _$identity);

  /// Serializes this PurchaseOrderPageDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is PurchaseOrderPageDto&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'PurchaseOrderPageDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class $PurchaseOrderPageDtoCopyWith<$Res>  {
  factory $PurchaseOrderPageDtoCopyWith(PurchaseOrderPageDto value, $Res Function(PurchaseOrderPageDto) _then) = _$PurchaseOrderPageDtoCopyWithImpl;
@useResult
$Res call({
 List<PurchaseOrderListItemDto> items, int totalCount, int pageNumber, int pageSize
});




}
/// @nodoc
class _$PurchaseOrderPageDtoCopyWithImpl<$Res>
    implements $PurchaseOrderPageDtoCopyWith<$Res> {
  _$PurchaseOrderPageDtoCopyWithImpl(this._self, this._then);

  final PurchaseOrderPageDto _self;
  final $Res Function(PurchaseOrderPageDto) _then;

/// Create a copy of PurchaseOrderPageDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}

}


/// Adds pattern-matching-related methods to [PurchaseOrderPageDto].
extension PurchaseOrderPageDtoPatterns on PurchaseOrderPageDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _PurchaseOrderPageDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _PurchaseOrderPageDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _PurchaseOrderPageDto value)  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderPageDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _PurchaseOrderPageDto value)?  $default,){
final _that = this;
switch (_that) {
case _PurchaseOrderPageDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( List<PurchaseOrderListItemDto> items,  int totalCount,  int pageNumber,  int pageSize)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _PurchaseOrderPageDto() when $default != null:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( List<PurchaseOrderListItemDto> items,  int totalCount,  int pageNumber,  int pageSize)  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderPageDto():
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( List<PurchaseOrderListItemDto> items,  int totalCount,  int pageNumber,  int pageSize)?  $default,) {final _that = this;
switch (_that) {
case _PurchaseOrderPageDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _PurchaseOrderPageDto implements PurchaseOrderPageDto {
  const _PurchaseOrderPageDto({final  List<PurchaseOrderListItemDto> items = const [], required this.totalCount, required this.pageNumber, required this.pageSize}): _items = items;
  factory _PurchaseOrderPageDto.fromJson(Map<String, dynamic> json) => _$PurchaseOrderPageDtoFromJson(json);

 final  List<PurchaseOrderListItemDto> _items;
@override@JsonKey() List<PurchaseOrderListItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override final  int totalCount;
@override final  int pageNumber;
@override final  int pageSize;

/// Create a copy of PurchaseOrderPageDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$PurchaseOrderPageDtoCopyWith<_PurchaseOrderPageDto> get copyWith => __$PurchaseOrderPageDtoCopyWithImpl<_PurchaseOrderPageDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$PurchaseOrderPageDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _PurchaseOrderPageDto&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'PurchaseOrderPageDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class _$PurchaseOrderPageDtoCopyWith<$Res> implements $PurchaseOrderPageDtoCopyWith<$Res> {
  factory _$PurchaseOrderPageDtoCopyWith(_PurchaseOrderPageDto value, $Res Function(_PurchaseOrderPageDto) _then) = __$PurchaseOrderPageDtoCopyWithImpl;
@override @useResult
$Res call({
 List<PurchaseOrderListItemDto> items, int totalCount, int pageNumber, int pageSize
});




}
/// @nodoc
class __$PurchaseOrderPageDtoCopyWithImpl<$Res>
    implements _$PurchaseOrderPageDtoCopyWith<$Res> {
  __$PurchaseOrderPageDtoCopyWithImpl(this._self, this._then);

  final _PurchaseOrderPageDto _self;
  final $Res Function(_PurchaseOrderPageDto) _then;

/// Create a copy of PurchaseOrderPageDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_PurchaseOrderPageDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<PurchaseOrderListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}


}

// dart format on
