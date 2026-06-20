// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_rules_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountRulesResponseDto {

@JsonKey(name: 'items') List<DiscountRuleListItemDto> get items;@JsonKey(name: 'totalCount') int get totalCount;@JsonKey(name: 'pageNumber') int get pageNumber;@JsonKey(name: 'pageSize') int get pageSize;@JsonKey(name: 'pageCount') int? get pageCount;
/// Create a copy of DiscountRulesResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountRulesResponseDtoCopyWith<DiscountRulesResponseDto> get copyWith => _$DiscountRulesResponseDtoCopyWithImpl<DiscountRulesResponseDto>(this as DiscountRulesResponseDto, _$identity);

  /// Serializes this DiscountRulesResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountRulesResponseDto&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize)&&(identical(other.pageCount, pageCount) || other.pageCount == pageCount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items),totalCount,pageNumber,pageSize,pageCount);

@override
String toString() {
  return 'DiscountRulesResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize, pageCount: $pageCount)';
}


}

/// @nodoc
abstract mixin class $DiscountRulesResponseDtoCopyWith<$Res>  {
  factory $DiscountRulesResponseDtoCopyWith(DiscountRulesResponseDto value, $Res Function(DiscountRulesResponseDto) _then) = _$DiscountRulesResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'items') List<DiscountRuleListItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize,@JsonKey(name: 'pageCount') int? pageCount
});




}
/// @nodoc
class _$DiscountRulesResponseDtoCopyWithImpl<$Res>
    implements $DiscountRulesResponseDtoCopyWith<$Res> {
  _$DiscountRulesResponseDtoCopyWithImpl(this._self, this._then);

  final DiscountRulesResponseDto _self;
  final $Res Function(DiscountRulesResponseDto) _then;

/// Create a copy of DiscountRulesResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,Object? pageCount = freezed,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<DiscountRuleListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,pageCount: freezed == pageCount ? _self.pageCount : pageCount // ignore: cast_nullable_to_non_nullable
as int?,
  ));
}

}


/// Adds pattern-matching-related methods to [DiscountRulesResponseDto].
extension DiscountRulesResponseDtoPatterns on DiscountRulesResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountRulesResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountRulesResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountRulesResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountRulesResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountRulesResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountRulesResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<DiscountRuleListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize, @JsonKey(name: 'pageCount')  int? pageCount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DiscountRulesResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize,_that.pageCount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<DiscountRuleListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize, @JsonKey(name: 'pageCount')  int? pageCount)  $default,) {final _that = this;
switch (_that) {
case _DiscountRulesResponseDto():
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize,_that.pageCount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'items')  List<DiscountRuleListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize, @JsonKey(name: 'pageCount')  int? pageCount)?  $default,) {final _that = this;
switch (_that) {
case _DiscountRulesResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize,_that.pageCount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountRulesResponseDto implements DiscountRulesResponseDto {
  const _DiscountRulesResponseDto({@JsonKey(name: 'items') final  List<DiscountRuleListItemDto> items = const [], @JsonKey(name: 'totalCount') required this.totalCount, @JsonKey(name: 'pageNumber') required this.pageNumber, @JsonKey(name: 'pageSize') required this.pageSize, @JsonKey(name: 'pageCount') this.pageCount}): _items = items;
  factory _DiscountRulesResponseDto.fromJson(Map<String, dynamic> json) => _$DiscountRulesResponseDtoFromJson(json);

 final  List<DiscountRuleListItemDto> _items;
@override@JsonKey(name: 'items') List<DiscountRuleListItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override@JsonKey(name: 'totalCount') final  int totalCount;
@override@JsonKey(name: 'pageNumber') final  int pageNumber;
@override@JsonKey(name: 'pageSize') final  int pageSize;
@override@JsonKey(name: 'pageCount') final  int? pageCount;

/// Create a copy of DiscountRulesResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountRulesResponseDtoCopyWith<_DiscountRulesResponseDto> get copyWith => __$DiscountRulesResponseDtoCopyWithImpl<_DiscountRulesResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountRulesResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountRulesResponseDto&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize)&&(identical(other.pageCount, pageCount) || other.pageCount == pageCount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items),totalCount,pageNumber,pageSize,pageCount);

@override
String toString() {
  return 'DiscountRulesResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize, pageCount: $pageCount)';
}


}

/// @nodoc
abstract mixin class _$DiscountRulesResponseDtoCopyWith<$Res> implements $DiscountRulesResponseDtoCopyWith<$Res> {
  factory _$DiscountRulesResponseDtoCopyWith(_DiscountRulesResponseDto value, $Res Function(_DiscountRulesResponseDto) _then) = __$DiscountRulesResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'items') List<DiscountRuleListItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize,@JsonKey(name: 'pageCount') int? pageCount
});




}
/// @nodoc
class __$DiscountRulesResponseDtoCopyWithImpl<$Res>
    implements _$DiscountRulesResponseDtoCopyWith<$Res> {
  __$DiscountRulesResponseDtoCopyWithImpl(this._self, this._then);

  final _DiscountRulesResponseDto _self;
  final $Res Function(_DiscountRulesResponseDto) _then;

/// Create a copy of DiscountRulesResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,Object? pageCount = freezed,}) {
  return _then(_DiscountRulesResponseDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<DiscountRuleListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,pageCount: freezed == pageCount ? _self.pageCount : pageCount // ignore: cast_nullable_to_non_nullable
as int?,
  ));
}


}

// dart format on
