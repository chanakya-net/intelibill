// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'inventory_adjustment_history_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$InventoryAdjustmentHistoryResponseDto {

@JsonKey(name: 'items') List<InventoryAdjustmentDto> get items;@JsonKey(name: 'totalCount') int get totalCount;@JsonKey(name: 'pageNumber') int get pageNumber;@JsonKey(name: 'pageSize') int get pageSize;
/// Create a copy of InventoryAdjustmentHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$InventoryAdjustmentHistoryResponseDtoCopyWith<InventoryAdjustmentHistoryResponseDto> get copyWith => _$InventoryAdjustmentHistoryResponseDtoCopyWithImpl<InventoryAdjustmentHistoryResponseDto>(this as InventoryAdjustmentHistoryResponseDto, _$identity);

  /// Serializes this InventoryAdjustmentHistoryResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is InventoryAdjustmentHistoryResponseDto&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'InventoryAdjustmentHistoryResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class $InventoryAdjustmentHistoryResponseDtoCopyWith<$Res>  {
  factory $InventoryAdjustmentHistoryResponseDtoCopyWith(InventoryAdjustmentHistoryResponseDto value, $Res Function(InventoryAdjustmentHistoryResponseDto) _then) = _$InventoryAdjustmentHistoryResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'items') List<InventoryAdjustmentDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize
});




}
/// @nodoc
class _$InventoryAdjustmentHistoryResponseDtoCopyWithImpl<$Res>
    implements $InventoryAdjustmentHistoryResponseDtoCopyWith<$Res> {
  _$InventoryAdjustmentHistoryResponseDtoCopyWithImpl(this._self, this._then);

  final InventoryAdjustmentHistoryResponseDto _self;
  final $Res Function(InventoryAdjustmentHistoryResponseDto) _then;

/// Create a copy of InventoryAdjustmentHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<InventoryAdjustmentDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}

}


/// Adds pattern-matching-related methods to [InventoryAdjustmentHistoryResponseDto].
extension InventoryAdjustmentHistoryResponseDtoPatterns on InventoryAdjustmentHistoryResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _InventoryAdjustmentHistoryResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _InventoryAdjustmentHistoryResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _InventoryAdjustmentHistoryResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _InventoryAdjustmentHistoryResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _InventoryAdjustmentHistoryResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _InventoryAdjustmentHistoryResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<InventoryAdjustmentDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _InventoryAdjustmentHistoryResponseDto() when $default != null:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<InventoryAdjustmentDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)  $default,) {final _that = this;
switch (_that) {
case _InventoryAdjustmentHistoryResponseDto():
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'items')  List<InventoryAdjustmentDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize)?  $default,) {final _that = this;
switch (_that) {
case _InventoryAdjustmentHistoryResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _InventoryAdjustmentHistoryResponseDto implements InventoryAdjustmentHistoryResponseDto {
  const _InventoryAdjustmentHistoryResponseDto({@JsonKey(name: 'items') final  List<InventoryAdjustmentDto> items = const [], @JsonKey(name: 'totalCount') required this.totalCount, @JsonKey(name: 'pageNumber') required this.pageNumber, @JsonKey(name: 'pageSize') required this.pageSize}): _items = items;
  factory _InventoryAdjustmentHistoryResponseDto.fromJson(Map<String, dynamic> json) => _$InventoryAdjustmentHistoryResponseDtoFromJson(json);

 final  List<InventoryAdjustmentDto> _items;
@override@JsonKey(name: 'items') List<InventoryAdjustmentDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override@JsonKey(name: 'totalCount') final  int totalCount;
@override@JsonKey(name: 'pageNumber') final  int pageNumber;
@override@JsonKey(name: 'pageSize') final  int pageSize;

/// Create a copy of InventoryAdjustmentHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$InventoryAdjustmentHistoryResponseDtoCopyWith<_InventoryAdjustmentHistoryResponseDto> get copyWith => __$InventoryAdjustmentHistoryResponseDtoCopyWithImpl<_InventoryAdjustmentHistoryResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$InventoryAdjustmentHistoryResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _InventoryAdjustmentHistoryResponseDto&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items),totalCount,pageNumber,pageSize);

@override
String toString() {
  return 'InventoryAdjustmentHistoryResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize)';
}


}

/// @nodoc
abstract mixin class _$InventoryAdjustmentHistoryResponseDtoCopyWith<$Res> implements $InventoryAdjustmentHistoryResponseDtoCopyWith<$Res> {
  factory _$InventoryAdjustmentHistoryResponseDtoCopyWith(_InventoryAdjustmentHistoryResponseDto value, $Res Function(_InventoryAdjustmentHistoryResponseDto) _then) = __$InventoryAdjustmentHistoryResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'items') List<InventoryAdjustmentDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize
});




}
/// @nodoc
class __$InventoryAdjustmentHistoryResponseDtoCopyWithImpl<$Res>
    implements _$InventoryAdjustmentHistoryResponseDtoCopyWith<$Res> {
  __$InventoryAdjustmentHistoryResponseDtoCopyWithImpl(this._self, this._then);

  final _InventoryAdjustmentHistoryResponseDto _self;
  final $Res Function(_InventoryAdjustmentHistoryResponseDto) _then;

/// Create a copy of InventoryAdjustmentHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,}) {
  return _then(_InventoryAdjustmentHistoryResponseDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<InventoryAdjustmentDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,
  ));
}


}

// dart format on
