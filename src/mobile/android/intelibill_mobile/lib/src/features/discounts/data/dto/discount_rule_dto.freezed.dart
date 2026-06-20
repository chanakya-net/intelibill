// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'discount_rule_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$DiscountRuleDto {

@JsonKey(name: 'id') String get id;@JsonKey(name: 'ruleType') String get ruleType;@JsonKey(name: 'name') String get name;@JsonKey(name: 'description') String? get description;@JsonKey(name: 'inventoryBatchId') String? get inventoryBatchId;@JsonKey(name: 'percentage') double get percentage;@JsonKey(name: 'thresholdAmount') double? get thresholdAmount;@JsonKey(name: 'startsAt') DateTime? get startsAt;@JsonKey(name: 'endsAt') DateTime? get endsAt;@JsonKey(name: 'isActive') bool get isActive;@JsonKey(name: 'disabledAt') DateTime? get disabledAt;@JsonKey(name: 'disabledReason') String? get disabledReason;@JsonKey(name: 'belowCostConfirmed') bool get belowCostConfirmed;@JsonKey(name: 'belowCostConfirmationReason') String? get belowCostConfirmationReason;@JsonKey(name: 'replacesRuleId') String? get replacesRuleId;@JsonKey(name: 'replacedByRuleId') String? get replacedByRuleId;@JsonKey(name: 'createdAt') DateTime get createdAt;@JsonKey(name: 'updatedAt') DateTime? get updatedAt;
/// Create a copy of DiscountRuleDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$DiscountRuleDtoCopyWith<DiscountRuleDto> get copyWith => _$DiscountRuleDtoCopyWithImpl<DiscountRuleDto>(this as DiscountRuleDto, _$identity);

  /// Serializes this DiscountRuleDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is DiscountRuleDto&&(identical(other.id, id) || other.id == id)&&(identical(other.ruleType, ruleType) || other.ruleType == ruleType)&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.percentage, percentage) || other.percentage == percentage)&&(identical(other.thresholdAmount, thresholdAmount) || other.thresholdAmount == thresholdAmount)&&(identical(other.startsAt, startsAt) || other.startsAt == startsAt)&&(identical(other.endsAt, endsAt) || other.endsAt == endsAt)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.disabledAt, disabledAt) || other.disabledAt == disabledAt)&&(identical(other.disabledReason, disabledReason) || other.disabledReason == disabledReason)&&(identical(other.belowCostConfirmed, belowCostConfirmed) || other.belowCostConfirmed == belowCostConfirmed)&&(identical(other.belowCostConfirmationReason, belowCostConfirmationReason) || other.belowCostConfirmationReason == belowCostConfirmationReason)&&(identical(other.replacesRuleId, replacesRuleId) || other.replacesRuleId == replacesRuleId)&&(identical(other.replacedByRuleId, replacedByRuleId) || other.replacedByRuleId == replacedByRuleId)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt)&&(identical(other.updatedAt, updatedAt) || other.updatedAt == updatedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,ruleType,name,description,inventoryBatchId,percentage,thresholdAmount,startsAt,endsAt,isActive,disabledAt,disabledReason,belowCostConfirmed,belowCostConfirmationReason,replacesRuleId,replacedByRuleId,createdAt,updatedAt);

@override
String toString() {
  return 'DiscountRuleDto(id: $id, ruleType: $ruleType, name: $name, description: $description, inventoryBatchId: $inventoryBatchId, percentage: $percentage, thresholdAmount: $thresholdAmount, startsAt: $startsAt, endsAt: $endsAt, isActive: $isActive, disabledAt: $disabledAt, disabledReason: $disabledReason, belowCostConfirmed: $belowCostConfirmed, belowCostConfirmationReason: $belowCostConfirmationReason, replacesRuleId: $replacesRuleId, replacedByRuleId: $replacedByRuleId, createdAt: $createdAt, updatedAt: $updatedAt)';
}


}

/// @nodoc
abstract mixin class $DiscountRuleDtoCopyWith<$Res>  {
  factory $DiscountRuleDtoCopyWith(DiscountRuleDto value, $Res Function(DiscountRuleDto) _then) = _$DiscountRuleDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'ruleType') String ruleType,@JsonKey(name: 'name') String name,@JsonKey(name: 'description') String? description,@JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,@JsonKey(name: 'percentage') double percentage,@JsonKey(name: 'thresholdAmount') double? thresholdAmount,@JsonKey(name: 'startsAt') DateTime? startsAt,@JsonKey(name: 'endsAt') DateTime? endsAt,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'disabledAt') DateTime? disabledAt,@JsonKey(name: 'disabledReason') String? disabledReason,@JsonKey(name: 'belowCostConfirmed') bool belowCostConfirmed,@JsonKey(name: 'belowCostConfirmationReason') String? belowCostConfirmationReason,@JsonKey(name: 'replacesRuleId') String? replacesRuleId,@JsonKey(name: 'replacedByRuleId') String? replacedByRuleId,@JsonKey(name: 'createdAt') DateTime createdAt,@JsonKey(name: 'updatedAt') DateTime? updatedAt
});




}
/// @nodoc
class _$DiscountRuleDtoCopyWithImpl<$Res>
    implements $DiscountRuleDtoCopyWith<$Res> {
  _$DiscountRuleDtoCopyWithImpl(this._self, this._then);

  final DiscountRuleDto _self;
  final $Res Function(DiscountRuleDto) _then;

/// Create a copy of DiscountRuleDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? ruleType = null,Object? name = null,Object? description = freezed,Object? inventoryBatchId = freezed,Object? percentage = null,Object? thresholdAmount = freezed,Object? startsAt = freezed,Object? endsAt = freezed,Object? isActive = null,Object? disabledAt = freezed,Object? disabledReason = freezed,Object? belowCostConfirmed = null,Object? belowCostConfirmationReason = freezed,Object? replacesRuleId = freezed,Object? replacedByRuleId = freezed,Object? createdAt = null,Object? updatedAt = freezed,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,ruleType: null == ruleType ? _self.ruleType : ruleType // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,inventoryBatchId: freezed == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String?,percentage: null == percentage ? _self.percentage : percentage // ignore: cast_nullable_to_non_nullable
as double,thresholdAmount: freezed == thresholdAmount ? _self.thresholdAmount : thresholdAmount // ignore: cast_nullable_to_non_nullable
as double?,startsAt: freezed == startsAt ? _self.startsAt : startsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,endsAt: freezed == endsAt ? _self.endsAt : endsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,disabledAt: freezed == disabledAt ? _self.disabledAt : disabledAt // ignore: cast_nullable_to_non_nullable
as DateTime?,disabledReason: freezed == disabledReason ? _self.disabledReason : disabledReason // ignore: cast_nullable_to_non_nullable
as String?,belowCostConfirmed: null == belowCostConfirmed ? _self.belowCostConfirmed : belowCostConfirmed // ignore: cast_nullable_to_non_nullable
as bool,belowCostConfirmationReason: freezed == belowCostConfirmationReason ? _self.belowCostConfirmationReason : belowCostConfirmationReason // ignore: cast_nullable_to_non_nullable
as String?,replacesRuleId: freezed == replacesRuleId ? _self.replacesRuleId : replacesRuleId // ignore: cast_nullable_to_non_nullable
as String?,replacedByRuleId: freezed == replacedByRuleId ? _self.replacedByRuleId : replacedByRuleId // ignore: cast_nullable_to_non_nullable
as String?,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,updatedAt: freezed == updatedAt ? _self.updatedAt : updatedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,
  ));
}

}


/// Adds pattern-matching-related methods to [DiscountRuleDto].
extension DiscountRuleDtoPatterns on DiscountRuleDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _DiscountRuleDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _DiscountRuleDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _DiscountRuleDto value)  $default,){
final _that = this;
switch (_that) {
case _DiscountRuleDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _DiscountRuleDto value)?  $default,){
final _that = this;
switch (_that) {
case _DiscountRuleDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'ruleType')  String ruleType, @JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'percentage')  double percentage, @JsonKey(name: 'thresholdAmount')  double? thresholdAmount, @JsonKey(name: 'startsAt')  DateTime? startsAt, @JsonKey(name: 'endsAt')  DateTime? endsAt, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'disabledAt')  DateTime? disabledAt, @JsonKey(name: 'disabledReason')  String? disabledReason, @JsonKey(name: 'belowCostConfirmed')  bool belowCostConfirmed, @JsonKey(name: 'belowCostConfirmationReason')  String? belowCostConfirmationReason, @JsonKey(name: 'replacesRuleId')  String? replacesRuleId, @JsonKey(name: 'replacedByRuleId')  String? replacedByRuleId, @JsonKey(name: 'createdAt')  DateTime createdAt, @JsonKey(name: 'updatedAt')  DateTime? updatedAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _DiscountRuleDto() when $default != null:
return $default(_that.id,_that.ruleType,_that.name,_that.description,_that.inventoryBatchId,_that.percentage,_that.thresholdAmount,_that.startsAt,_that.endsAt,_that.isActive,_that.disabledAt,_that.disabledReason,_that.belowCostConfirmed,_that.belowCostConfirmationReason,_that.replacesRuleId,_that.replacedByRuleId,_that.createdAt,_that.updatedAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'ruleType')  String ruleType, @JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'percentage')  double percentage, @JsonKey(name: 'thresholdAmount')  double? thresholdAmount, @JsonKey(name: 'startsAt')  DateTime? startsAt, @JsonKey(name: 'endsAt')  DateTime? endsAt, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'disabledAt')  DateTime? disabledAt, @JsonKey(name: 'disabledReason')  String? disabledReason, @JsonKey(name: 'belowCostConfirmed')  bool belowCostConfirmed, @JsonKey(name: 'belowCostConfirmationReason')  String? belowCostConfirmationReason, @JsonKey(name: 'replacesRuleId')  String? replacesRuleId, @JsonKey(name: 'replacedByRuleId')  String? replacedByRuleId, @JsonKey(name: 'createdAt')  DateTime createdAt, @JsonKey(name: 'updatedAt')  DateTime? updatedAt)  $default,) {final _that = this;
switch (_that) {
case _DiscountRuleDto():
return $default(_that.id,_that.ruleType,_that.name,_that.description,_that.inventoryBatchId,_that.percentage,_that.thresholdAmount,_that.startsAt,_that.endsAt,_that.isActive,_that.disabledAt,_that.disabledReason,_that.belowCostConfirmed,_that.belowCostConfirmationReason,_that.replacesRuleId,_that.replacedByRuleId,_that.createdAt,_that.updatedAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'id')  String id, @JsonKey(name: 'ruleType')  String ruleType, @JsonKey(name: 'name')  String name, @JsonKey(name: 'description')  String? description, @JsonKey(name: 'inventoryBatchId')  String? inventoryBatchId, @JsonKey(name: 'percentage')  double percentage, @JsonKey(name: 'thresholdAmount')  double? thresholdAmount, @JsonKey(name: 'startsAt')  DateTime? startsAt, @JsonKey(name: 'endsAt')  DateTime? endsAt, @JsonKey(name: 'isActive')  bool isActive, @JsonKey(name: 'disabledAt')  DateTime? disabledAt, @JsonKey(name: 'disabledReason')  String? disabledReason, @JsonKey(name: 'belowCostConfirmed')  bool belowCostConfirmed, @JsonKey(name: 'belowCostConfirmationReason')  String? belowCostConfirmationReason, @JsonKey(name: 'replacesRuleId')  String? replacesRuleId, @JsonKey(name: 'replacedByRuleId')  String? replacedByRuleId, @JsonKey(name: 'createdAt')  DateTime createdAt, @JsonKey(name: 'updatedAt')  DateTime? updatedAt)?  $default,) {final _that = this;
switch (_that) {
case _DiscountRuleDto() when $default != null:
return $default(_that.id,_that.ruleType,_that.name,_that.description,_that.inventoryBatchId,_that.percentage,_that.thresholdAmount,_that.startsAt,_that.endsAt,_that.isActive,_that.disabledAt,_that.disabledReason,_that.belowCostConfirmed,_that.belowCostConfirmationReason,_that.replacesRuleId,_that.replacedByRuleId,_that.createdAt,_that.updatedAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _DiscountRuleDto implements DiscountRuleDto {
  const _DiscountRuleDto({@JsonKey(name: 'id') required this.id, @JsonKey(name: 'ruleType') required this.ruleType, @JsonKey(name: 'name') required this.name, @JsonKey(name: 'description') this.description, @JsonKey(name: 'inventoryBatchId') this.inventoryBatchId, @JsonKey(name: 'percentage') required this.percentage, @JsonKey(name: 'thresholdAmount') this.thresholdAmount, @JsonKey(name: 'startsAt') this.startsAt, @JsonKey(name: 'endsAt') this.endsAt, @JsonKey(name: 'isActive') required this.isActive, @JsonKey(name: 'disabledAt') this.disabledAt, @JsonKey(name: 'disabledReason') this.disabledReason, @JsonKey(name: 'belowCostConfirmed') required this.belowCostConfirmed, @JsonKey(name: 'belowCostConfirmationReason') this.belowCostConfirmationReason, @JsonKey(name: 'replacesRuleId') this.replacesRuleId, @JsonKey(name: 'replacedByRuleId') this.replacedByRuleId, @JsonKey(name: 'createdAt') required this.createdAt, @JsonKey(name: 'updatedAt') this.updatedAt});
  factory _DiscountRuleDto.fromJson(Map<String, dynamic> json) => _$DiscountRuleDtoFromJson(json);

@override@JsonKey(name: 'id') final  String id;
@override@JsonKey(name: 'ruleType') final  String ruleType;
@override@JsonKey(name: 'name') final  String name;
@override@JsonKey(name: 'description') final  String? description;
@override@JsonKey(name: 'inventoryBatchId') final  String? inventoryBatchId;
@override@JsonKey(name: 'percentage') final  double percentage;
@override@JsonKey(name: 'thresholdAmount') final  double? thresholdAmount;
@override@JsonKey(name: 'startsAt') final  DateTime? startsAt;
@override@JsonKey(name: 'endsAt') final  DateTime? endsAt;
@override@JsonKey(name: 'isActive') final  bool isActive;
@override@JsonKey(name: 'disabledAt') final  DateTime? disabledAt;
@override@JsonKey(name: 'disabledReason') final  String? disabledReason;
@override@JsonKey(name: 'belowCostConfirmed') final  bool belowCostConfirmed;
@override@JsonKey(name: 'belowCostConfirmationReason') final  String? belowCostConfirmationReason;
@override@JsonKey(name: 'replacesRuleId') final  String? replacesRuleId;
@override@JsonKey(name: 'replacedByRuleId') final  String? replacedByRuleId;
@override@JsonKey(name: 'createdAt') final  DateTime createdAt;
@override@JsonKey(name: 'updatedAt') final  DateTime? updatedAt;

/// Create a copy of DiscountRuleDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$DiscountRuleDtoCopyWith<_DiscountRuleDto> get copyWith => __$DiscountRuleDtoCopyWithImpl<_DiscountRuleDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$DiscountRuleDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _DiscountRuleDto&&(identical(other.id, id) || other.id == id)&&(identical(other.ruleType, ruleType) || other.ruleType == ruleType)&&(identical(other.name, name) || other.name == name)&&(identical(other.description, description) || other.description == description)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.percentage, percentage) || other.percentage == percentage)&&(identical(other.thresholdAmount, thresholdAmount) || other.thresholdAmount == thresholdAmount)&&(identical(other.startsAt, startsAt) || other.startsAt == startsAt)&&(identical(other.endsAt, endsAt) || other.endsAt == endsAt)&&(identical(other.isActive, isActive) || other.isActive == isActive)&&(identical(other.disabledAt, disabledAt) || other.disabledAt == disabledAt)&&(identical(other.disabledReason, disabledReason) || other.disabledReason == disabledReason)&&(identical(other.belowCostConfirmed, belowCostConfirmed) || other.belowCostConfirmed == belowCostConfirmed)&&(identical(other.belowCostConfirmationReason, belowCostConfirmationReason) || other.belowCostConfirmationReason == belowCostConfirmationReason)&&(identical(other.replacesRuleId, replacesRuleId) || other.replacesRuleId == replacesRuleId)&&(identical(other.replacedByRuleId, replacedByRuleId) || other.replacedByRuleId == replacedByRuleId)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt)&&(identical(other.updatedAt, updatedAt) || other.updatedAt == updatedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,ruleType,name,description,inventoryBatchId,percentage,thresholdAmount,startsAt,endsAt,isActive,disabledAt,disabledReason,belowCostConfirmed,belowCostConfirmationReason,replacesRuleId,replacedByRuleId,createdAt,updatedAt);

@override
String toString() {
  return 'DiscountRuleDto(id: $id, ruleType: $ruleType, name: $name, description: $description, inventoryBatchId: $inventoryBatchId, percentage: $percentage, thresholdAmount: $thresholdAmount, startsAt: $startsAt, endsAt: $endsAt, isActive: $isActive, disabledAt: $disabledAt, disabledReason: $disabledReason, belowCostConfirmed: $belowCostConfirmed, belowCostConfirmationReason: $belowCostConfirmationReason, replacesRuleId: $replacesRuleId, replacedByRuleId: $replacedByRuleId, createdAt: $createdAt, updatedAt: $updatedAt)';
}


}

/// @nodoc
abstract mixin class _$DiscountRuleDtoCopyWith<$Res> implements $DiscountRuleDtoCopyWith<$Res> {
  factory _$DiscountRuleDtoCopyWith(_DiscountRuleDto value, $Res Function(_DiscountRuleDto) _then) = __$DiscountRuleDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'id') String id,@JsonKey(name: 'ruleType') String ruleType,@JsonKey(name: 'name') String name,@JsonKey(name: 'description') String? description,@JsonKey(name: 'inventoryBatchId') String? inventoryBatchId,@JsonKey(name: 'percentage') double percentage,@JsonKey(name: 'thresholdAmount') double? thresholdAmount,@JsonKey(name: 'startsAt') DateTime? startsAt,@JsonKey(name: 'endsAt') DateTime? endsAt,@JsonKey(name: 'isActive') bool isActive,@JsonKey(name: 'disabledAt') DateTime? disabledAt,@JsonKey(name: 'disabledReason') String? disabledReason,@JsonKey(name: 'belowCostConfirmed') bool belowCostConfirmed,@JsonKey(name: 'belowCostConfirmationReason') String? belowCostConfirmationReason,@JsonKey(name: 'replacesRuleId') String? replacesRuleId,@JsonKey(name: 'replacedByRuleId') String? replacedByRuleId,@JsonKey(name: 'createdAt') DateTime createdAt,@JsonKey(name: 'updatedAt') DateTime? updatedAt
});




}
/// @nodoc
class __$DiscountRuleDtoCopyWithImpl<$Res>
    implements _$DiscountRuleDtoCopyWith<$Res> {
  __$DiscountRuleDtoCopyWithImpl(this._self, this._then);

  final _DiscountRuleDto _self;
  final $Res Function(_DiscountRuleDto) _then;

/// Create a copy of DiscountRuleDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? ruleType = null,Object? name = null,Object? description = freezed,Object? inventoryBatchId = freezed,Object? percentage = null,Object? thresholdAmount = freezed,Object? startsAt = freezed,Object? endsAt = freezed,Object? isActive = null,Object? disabledAt = freezed,Object? disabledReason = freezed,Object? belowCostConfirmed = null,Object? belowCostConfirmationReason = freezed,Object? replacesRuleId = freezed,Object? replacedByRuleId = freezed,Object? createdAt = null,Object? updatedAt = freezed,}) {
  return _then(_DiscountRuleDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,ruleType: null == ruleType ? _self.ruleType : ruleType // ignore: cast_nullable_to_non_nullable
as String,name: null == name ? _self.name : name // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,inventoryBatchId: freezed == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String?,percentage: null == percentage ? _self.percentage : percentage // ignore: cast_nullable_to_non_nullable
as double,thresholdAmount: freezed == thresholdAmount ? _self.thresholdAmount : thresholdAmount // ignore: cast_nullable_to_non_nullable
as double?,startsAt: freezed == startsAt ? _self.startsAt : startsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,endsAt: freezed == endsAt ? _self.endsAt : endsAt // ignore: cast_nullable_to_non_nullable
as DateTime?,isActive: null == isActive ? _self.isActive : isActive // ignore: cast_nullable_to_non_nullable
as bool,disabledAt: freezed == disabledAt ? _self.disabledAt : disabledAt // ignore: cast_nullable_to_non_nullable
as DateTime?,disabledReason: freezed == disabledReason ? _self.disabledReason : disabledReason // ignore: cast_nullable_to_non_nullable
as String?,belowCostConfirmed: null == belowCostConfirmed ? _self.belowCostConfirmed : belowCostConfirmed // ignore: cast_nullable_to_non_nullable
as bool,belowCostConfirmationReason: freezed == belowCostConfirmationReason ? _self.belowCostConfirmationReason : belowCostConfirmationReason // ignore: cast_nullable_to_non_nullable
as String?,replacesRuleId: freezed == replacesRuleId ? _self.replacesRuleId : replacesRuleId // ignore: cast_nullable_to_non_nullable
as String?,replacedByRuleId: freezed == replacedByRuleId ? _self.replacedByRuleId : replacedByRuleId // ignore: cast_nullable_to_non_nullable
as String?,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,updatedAt: freezed == updatedAt ? _self.updatedAt : updatedAt // ignore: cast_nullable_to_non_nullable
as DateTime?,
  ));
}


}

// dart format on
