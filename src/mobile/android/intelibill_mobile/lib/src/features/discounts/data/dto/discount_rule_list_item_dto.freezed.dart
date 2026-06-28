// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_rule_list_item_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountRuleListItemDto {

@JsonKey(name: 'id') String get id;@JsonKey(name: 'ruleType') String get ruleType;@JsonKey(name: 'name') String get name;@JsonKey(name: 'isActive') bool get isActive;@JsonKey(name: 'startsAt') DateTime? get startsAt;@JsonKey(name: 'endsAt') DateTime? get endsAt;@JsonKey(name: 'createdAt') DateTime get createdAt;
/// Create a copy of DiscountRuleListItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountRuleListItemDtoCopyWith<DiscountRuleListItemDto> get copyWith => _$DiscountRuleListItemDtoCopyWithImpl<DiscountRuleListItemDto>(this as DiscountRuleListItemDto, _$identity);

  /// Serializes this DiscountRuleListItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountRuleListItemDto&&(identical(other.id, id) || other.id == id)&&(identical(other.ruleType, ruleType) || other.ruleType == ruleType)&&(identical(other.name, name) || other.name == name)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.startsAt, startsAt) || other.startsAt == startsAt)&&(identical(other.endsAt, endsAt) || other.endsAt == endsAt)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,ruleType,name,isActive,startsAt,endsAt,createdAt);

@override
String toString() {
  return 'DiscountRuleListItemDto(id: $id, ruleType: $ruleType, name: $name, isActive: $isActive, startsAt: $startsAt, endsAt: $endsAt, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class $DiscountRuleListItemDtoCopyWith<$Res>  {
  factory $DiscountRuleListItemDtoCopyWith(DiscountRuleListItemDto value, $Res Function(DiscountRuleListItemDto) _then) = _$DiscountRuleListItemDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'ruleType') String ruleType,@JsonKey(name: 'name') String name,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'startsAt') DateTime? startsAt,@JsonKey(name: 'endsAt') DateTime? endsAt,@JsonKey(name: 'createdAt') DateTime createdAt
});




}
/// @nodoc
class _$DiscountRuleListItemDtoCopyWithImpl<$Res>
    implements $DiscountRuleListItemDtoCopyWith<$Res> {
  _$DiscountRuleListItemDtoCopyWithImpl(this._self, this._then);

  final DiscountRuleListItemDto _self;
  final $Res Function(DiscountRuleListItemDto) _then;

/// Create a copy of DiscountRuleListItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? ruleType = null,Object? name = null,Object? isActive = null,Object? startsAt = freezed,Object? endsAt = freezed,Object? createdAt = null,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,ruleType: null == ruleType ? _self.ruleType : ruleType // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,startsAt: freezed == startsAt ? _self.startsAt : startsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,endsAt: freezed == endsAt ? _self.endsAt : endsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [DiscountRuleListItemDto].
extension DiscountRuleListItemDtoPatterns on DiscountRuleListItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountRuleListItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountRuleListItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountRuleListItemDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountRuleListItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountRuleListItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountRuleListItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'ruleType')  String ruleType, @JsonKey(name: 'name')  String name, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'startsAt')  DateTime? startsAt, @JsonKey(name: 'endsAt')  DateTime? endsAt, @JsonKey(name: 'createdAt')  DateTime createdAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DiscountRuleListItemDto() when $default != null:
return $default(_that.id,_that.ruleType,_that.name,_that.isActive,_that.startsAt,_that.endsAt,_that.createdAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'ruleType')  String ruleType, @JsonKey(name: 'name')  String name, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'startsAt')  DateTime? startsAt, @JsonKey(name: 'endsAt')  DateTime? endsAt, @JsonKey(name: 'createdAt')  DateTime createdAt)  $default,) {final _that = this;
switch (_that) {
case _DiscountRuleListItemDto():
return $default(_that.id,_that.ruleType,_that.name,_that.isActive,_that.startsAt,_that.endsAt,_that.createdAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'ruleType')  String ruleType, @JsonKey(name: 'name')  String name, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'startsAt')  DateTime? startsAt, @JsonKey(name: 'endsAt')  DateTime? endsAt, @JsonKey(name: 'createdAt')  DateTime createdAt)?  $default,) {final _that = this;
switch (_that) {
case _DiscountRuleListItemDto() when $default != null:
return $default(_that.id,_that.ruleType,_that.name,_that.isActive,_that.startsAt,_that.endsAt,_that.createdAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountRuleListItemDto implements DiscountRuleListItemDto {
  const _DiscountRuleListItemDto({@JsonKey(name: 'id') required this.id, @JsonKey(name: 'ruleType') required this.ruleType, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'isActive') required this.isActive, @JsonKey(name: 'startsAt') this.startsAt, @JsonKey(name: 'endsAt') this.endsAt, @JsonKey(name: 'createdAt') required this.createdAt});
  factory _DiscountRuleListItemDto.fromJson(Map<String, dynamic> json) => _$DiscountRuleListItemDtoFromJson(json);

@override@JsonKey(name: 'id') final  String id;
@override@JsonKey(name: 'ruleType') final  String ruleType;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'isActive') final  bool isActive;
@override@JsonKey(name: 'startsAt') final  DateTime? startsAt;
@override@JsonKey(name: 'endsAt') final  DateTime? endsAt;
@override@JsonKey(name: 'createdAt') final  DateTime createdAt;

/// Create a copy of DiscountRuleListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountRuleListItemDtoCopyWith<_DiscountRuleListItemDto> get copyWith => __$DiscountRuleListItemDtoCopyWithImpl<_DiscountRuleListItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountRuleListItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountRuleListItemDto&&(identical(other.id, id) || other.id == id)&&(identical(other.ruleType, ruleType) || other.ruleType == ruleType)&&(identical(other.name, name) || other.name == name)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.startsAt, startsAt) || other.startsAt == startsAt)&&(identical(other.endsAt, endsAt) || other.endsAt == endsAt)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,ruleType,name,isActive,startsAt,endsAt,createdAt);

@override
String toString() {
  return 'DiscountRuleListItemDto(id: $id, ruleType: $ruleType, name: $name, isActive: $isActive, startsAt: $startsAt, endsAt: $endsAt, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class _$DiscountRuleListItemDtoCopyWith<$Res> implements $DiscountRuleListItemDtoCopyWith<$Res> {
  factory _$DiscountRuleListItemDtoCopyWith(_DiscountRuleListItemDto value, $Res Function(_DiscountRuleListItemDto) _then) = __$DiscountRuleListItemDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'ruleType') String ruleType,@JsonKey(name: 'name') String name,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'startsAt') DateTime? startsAt,@JsonKey(name: 'endsAt') DateTime? endsAt,@JsonKey(name: 'createdAt') DateTime createdAt
});




}
/// @nodoc
class __$DiscountRuleListItemDtoCopyWithImpl<$Res>
    implements _$DiscountRuleListItemDtoCopyWith<$Res> {
  __$DiscountRuleListItemDtoCopyWithImpl(this._self, this._then);

  final _DiscountRuleListItemDto _self;
  final $Res Function(_DiscountRuleListItemDto) _then;

/// Create a copy of DiscountRuleListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? ruleType = null,Object? name = null,Object? isActive = null,Object? startsAt = freezed,Object? endsAt = freezed,Object? createdAt = null,}) {
  return _then(_DiscountRuleListItemDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,ruleType: null == ruleType ? _self.ruleType : ruleType // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,startsAt: freezed == startsAt ? _self.startsAt : startsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,endsAt: freezed == endsAt ? _self.endsAt : endsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}

// dart format on
