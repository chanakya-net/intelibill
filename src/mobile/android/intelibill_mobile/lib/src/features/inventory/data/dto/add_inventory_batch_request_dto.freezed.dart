// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'add_inventory_batch_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AddInventoryBatchRequestDto {

@JsonKey(name: 'items') List<AddInventoryBatchRowDto> get items;
/// Create a copy of AddInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryBatchRequestDtoCopyWith<AddInventoryBatchRequestDto> get copyWith => _$AddInventoryBatchRequestDtoCopyWithImpl<AddInventoryBatchRequestDto>(this as AddInventoryBatchRequestDto, _$identity);

  /// Serializes this AddInventoryBatchRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryBatchRequestDto&&const DeepCollectionEquality().equals(other.items, items));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items));

@override
String toString() {
  return 'AddInventoryBatchRequestDto(items: $items)';
}


}

/// @nodoc
abstract mixin class $AddInventoryBatchRequestDtoCopyWith<$Res>  {
  factory $AddInventoryBatchRequestDtoCopyWith(AddInventoryBatchRequestDto value, $Res Function(AddInventoryBatchRequestDto) _then) = _$AddInventoryBatchRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'items') List<AddInventoryBatchRowDto> items
});




}
/// @nodoc
class _$AddInventoryBatchRequestDtoCopyWithImpl<$Res>
    implements $AddInventoryBatchRequestDtoCopyWith<$Res> {
  _$AddInventoryBatchRequestDtoCopyWithImpl(this._self, this._then);

  final AddInventoryBatchRequestDto _self;
  final $Res Function(AddInventoryBatchRequestDto) _then;

/// Create a copy of AddInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchRowDto>,
  ));
}

}


/// Adds pattern-matching-related methods to [AddInventoryBatchRequestDto].
extension AddInventoryBatchRequestDtoPatterns on AddInventoryBatchRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryBatchRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryBatchRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryBatchRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryBatchRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<AddInventoryBatchRowDto> items)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryBatchRequestDto() when $default != null:
return $default(_that.items);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<AddInventoryBatchRowDto> items)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchRequestDto():
return $default(_that.items);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'items')  List<AddInventoryBatchRowDto> items)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchRequestDto() when $default != null:
return $default(_that.items);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryBatchRequestDto implements AddInventoryBatchRequestDto {
  const _AddInventoryBatchRequestDto({@JsonKey(name: 'items') required final  List<AddInventoryBatchRowDto> items}): _items = items;
  factory _AddInventoryBatchRequestDto.fromJson(Map<String, dynamic> json) => _$AddInventoryBatchRequestDtoFromJson(json);

 final  List<AddInventoryBatchRowDto> _items;
@override@JsonKey(name: 'items') List<AddInventoryBatchRowDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}


/// Create a copy of AddInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryBatchRequestDtoCopyWith<_AddInventoryBatchRequestDto> get copyWith => __$AddInventoryBatchRequestDtoCopyWithImpl<_AddInventoryBatchRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryBatchRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryBatchRequestDto&&const DeepCollectionEquality().equals(other._items, _items));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items));

@override
String toString() {
  return 'AddInventoryBatchRequestDto(items: $items)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryBatchRequestDtoCopyWith<$Res> implements $AddInventoryBatchRequestDtoCopyWith<$Res> {
  factory _$AddInventoryBatchRequestDtoCopyWith(_AddInventoryBatchRequestDto value, $Res Function(_AddInventoryBatchRequestDto) _then) = __$AddInventoryBatchRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'items') List<AddInventoryBatchRowDto> items
});




}
/// @nodoc
class __$AddInventoryBatchRequestDtoCopyWithImpl<$Res>
    implements _$AddInventoryBatchRequestDtoCopyWith<$Res> {
  __$AddInventoryBatchRequestDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryBatchRequestDto _self;
  final $Res Function(_AddInventoryBatchRequestDto) _then;

/// Create a copy of AddInventoryBatchRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,}) {
  return _then(_AddInventoryBatchRequestDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchRowDto>,
  ));
}


}

// dart format on
