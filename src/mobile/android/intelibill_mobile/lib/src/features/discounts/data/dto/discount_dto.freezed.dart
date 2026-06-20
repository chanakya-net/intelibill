// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountDto {

@JsonKey(name: 'discountId') String get discountId;@JsonKey(name: 'name') String get name;@JsonKey(name: 'discountType') String get discountType;@JsonKey(name: 'discountValue') double get discountValue;@JsonKey(name: 'batchPercentage') double? get batchPercentage;@JsonKey(name: 'isEnabled') bool get isEnabled;@JsonKey(name: 'createdAt') String get createdAt;
/// Create a copy of DiscountDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountDtoCopyWith<DiscountDto> get copyWith => _$DiscountDtoCopyWithImpl<DiscountDto>(this as DiscountDto, _$identity);

  /// Serializes this DiscountDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountDto&&(identical(other.discountId, discountId) || other.discountId == discountId)&&(identical(other.name, name) || other.name == name)&&(identical(other.discountType, discountType) || other.discountType == discountType)&&(identical(other.discountValue, discountValue) || other.discountValue == discountValue)&&(identical(other.batchPercentage, batchPercentage) || other.batchPercentage == batchPercentage)&&(identical(other.isEnabled, isEnabled) || other.isEnabled == isEnabled)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,discountId,name,discountType,discountValue,batchPercentage,isEnabled,createdAt);

@override
String toString() {
  return 'DiscountDto(discountId: $discountId, name: $name, discountType: $discountType, discountValue: $discountValue, batchPercentage: $batchPercentage, isEnabled: $isEnabled, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class $DiscountDtoCopyWith<$Res>  {
  factory $DiscountDtoCopyWith(DiscountDto value, $Res Function(DiscountDto) _then) = _$DiscountDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'discountId') String discountId,@JsonKey(name: 'name') String name,@JsonKey(name: 'discountType') String discountType,@JsonKey(name: 'discountValue') double discountValue,@JsonKey(name: 'batchPercentage') double? batchPercentage,@JsonKey(name: 'isEnabled') bool isEnabled,@JsonKey(name: 'createdAt') String createdAt
});




}
/// @nodoc
class _$DiscountDtoCopyWithImpl<$Res>
    implements $DiscountDtoCopyWith<$Res> {
  _$DiscountDtoCopyWithImpl(this._self, this._then);

  final DiscountDto _self;
  final $Res Function(DiscountDto) _then;

/// Create a copy of DiscountDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? discountId = null,Object? name = null,Object? discountType = null,Object? discountValue = null,Object? batchPercentage = freezed,Object? isEnabled = null,Object? createdAt = null,}) {
  return _then(_self.copyWith(
discountId: null == discountId ? _self.discountId : discountId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,discountType: null == discountType ? _self.discountType : discountType // ignore: cast_nullable_to_non_nullable
as String,discountValue: null == discountValue ? _self.discountValue : discountValue // ignore: cast_nullable_to_non_nullable
as double,batchPercentage: freezed == batchPercentage ? _self.batchPercentage : batchPercentage // ignore: cast_nullable_to_non_nullable
as double?,isEnabled: null == isEnabled ? _self.isEnabled : isEnabled // ignore: cast_nullable_to_non_nullable
as bool,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [DiscountDto].
extension DiscountDtoPatterns on DiscountDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'discountId')  String discountId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'discountType')  String discountType, @JsonKey(name: 'discountValue')  double discountValue, @JsonKey(name: 'batchPercentage')  double? batchPercentage, @JsonKey(name: 'isEnabled')  bool isEnabled, @JsonKey(name: 'createdAt')  String createdAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DiscountDto() when $default != null:
return $default(_that.discountId,_that.name,_that.discountType,_that.discountValue,_that.batchPercentage,_that.isEnabled,_that.createdAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'discountId')  String discountId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'discountType')  String discountType, @JsonKey(name: 'discountValue')  double discountValue, @JsonKey(name: 'batchPercentage')  double? batchPercentage, @JsonKey(name: 'isEnabled')  bool isEnabled, @JsonKey(name: 'createdAt')  String createdAt)  $default,) {final _that = this;
switch (_that) {
case _DiscountDto():
return $default(_that.discountId,_that.name,_that.discountType,_that.discountValue,_that.batchPercentage,_that.isEnabled,_that.createdAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'discountId')  String discountId, @JsonKey(name: 'name')  String name, @JsonKey(name: 'discountType')  String discountType, @JsonKey(name: 'discountValue')  double discountValue, @JsonKey(name: 'batchPercentage')  double? batchPercentage, @JsonKey(name: 'isEnabled')  bool isEnabled, @JsonKey(name: 'createdAt')  String createdAt)?  $default,) {final _that = this;
switch (_that) {
case _DiscountDto() when $default != null:
return $default(_that.discountId,_that.name,_that.discountType,_that.discountValue,_that.batchPercentage,_that.isEnabled,_that.createdAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountDto implements DiscountDto {
  const _DiscountDto({@JsonKey(name: 'discountId') required this.discountId, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'discountType') required this.discountType, @JsonKey(name: 'discountValue') required this.discountValue, @JsonKey(name: 'batchPercentage') required this.batchPercentage, @JsonKey(name: 'isEnabled') required this.isEnabled, @JsonKey(name: 'createdAt') required this.createdAt});
  factory _DiscountDto.fromJson(Map<String, dynamic> json) => _$DiscountDtoFromJson(json);

@override@JsonKey(name: 'discountId') final  String discountId;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'discountType') final  String discountType;
@override@JsonKey(name: 'discountValue') final  double discountValue;
@override@JsonKey(name: 'batchPercentage') final  double? batchPercentage;
@override@JsonKey(name: 'isEnabled') final  bool isEnabled;
@override@JsonKey(name: 'createdAt') final  String createdAt;

/// Create a copy of DiscountDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountDtoCopyWith<_DiscountDto> get copyWith => __$DiscountDtoCopyWithImpl<_DiscountDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountDto&&(identical(other.discountId, discountId) || other.discountId == discountId)&&(identical(other.name, name) || other.name == name)&&(identical(other.discountType, discountType) || other.discountType == discountType)&&(identical(other.discountValue, discountValue) || other.discountValue == discountValue)&&(identical(other.batchPercentage, batchPercentage) || other.batchPercentage == batchPercentage)&&(identical(other.isEnabled, isEnabled) || other.isEnabled == isEnabled)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,discountId,name,discountType,discountValue,batchPercentage,isEnabled,createdAt);

@override
String toString() {
  return 'DiscountDto(discountId: $discountId, name: $name, discountType: $discountType, discountValue: $discountValue, batchPercentage: $batchPercentage, isEnabled: $isEnabled, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class _$DiscountDtoCopyWith<$Res> implements $DiscountDtoCopyWith<$Res> {
  factory _$DiscountDtoCopyWith(_DiscountDto value, $Res Function(_DiscountDto) _then) = __$DiscountDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'discountId') String discountId,@JsonKey(name: 'name') String name,@JsonKey(name: 'discountType') String discountType,@JsonKey(name: 'discountValue') double discountValue,@JsonKey(name: 'batchPercentage') double? batchPercentage,@JsonKey(name: 'isEnabled') bool isEnabled,@JsonKey(name: 'createdAt') String createdAt
});




}
/// @nodoc
class __$DiscountDtoCopyWithImpl<$Res>
    implements _$DiscountDtoCopyWith<$Res> {
  __$DiscountDtoCopyWithImpl(this._self, this._then);

  final _DiscountDto _self;
  final $Res Function(_DiscountDto) _then;

/// Create a copy of DiscountDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? discountId = null,Object? name = null,Object? discountType = null,Object? discountValue = null,Object? batchPercentage = freezed,Object? isEnabled = null,Object? createdAt = null,}) {
  return _then(_DiscountDto(
discountId: null == discountId ? _self.discountId : discountId // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,discountType: null == discountType ? _self.discountType : discountType // ignore: cast_nullable_to_non_nullable
as String,discountValue: null == discountValue ? _self.discountValue : discountValue // ignore: cast_nullable_to_non_nullable
as double,batchPercentage: freezed == batchPercentage ? _self.batchPercentage : batchPercentage // ignore: cast_nullable_to_non_nullable
as double?,isEnabled: null == isEnabled ? _self.isEnabled : isEnabled // ignore: cast_nullable_to_non_nullable
as bool,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
