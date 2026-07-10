// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'expense_detail_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ExpenseDetailDto {

 String get id; String get shopId; String get categoryId; String get categoryName; double get amount; String get paidTo; String? get description; DateTime get expenseDate; String get actorUserId; bool get isVoided; String? get originalExpenseId; String? get supplierLedgerEntryId; DateTime get createdAt;
/// Create a copy of ExpenseDetailDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ExpenseDetailDtoCopyWith<ExpenseDetailDto> get copyWith => _$ExpenseDetailDtoCopyWithImpl<ExpenseDetailDto>(this as ExpenseDetailDto, _$identity);

  /// Serializes this ExpenseDetailDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ExpenseDetailDto&&(identical(other.id, id) || other.id == id)&&(identical(other.shopId, shopId) || other.shopId == shopId)&&(identical(other.categoryId, categoryId) || other.categoryId == categoryId)&&(identical(other.categoryName, categoryName) || other.categoryName == categoryName)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.paidTo, paidTo) || other.paidTo == paidTo)&&(identical(other.description, description) || other.description == description)&&(identical(other.expenseDate, expenseDate) || other.expenseDate == expenseDate)&&(identical(other.actorUserId, actorUserId) || other.actorUserId == actorUserId)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.originalExpenseId, originalExpenseId) || other.originalExpenseId == originalExpenseId)&&(identical(other.supplierLedgerEntryId, supplierLedgerEntryId) || other.supplierLedgerEntryId == supplierLedgerEntryId)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,shopId,categoryId,categoryName,amount,paidTo,description,expenseDate,actorUserId,isVoided,originalExpenseId,supplierLedgerEntryId,createdAt);

@override
String toString() {
  return 'ExpenseDetailDto(id: $id, shopId: $shopId, categoryId: $categoryId, categoryName: $categoryName, amount: $amount, paidTo: $paidTo, description: $description, expenseDate: $expenseDate, actorUserId: $actorUserId, isVoided: $isVoided, originalExpenseId: $originalExpenseId, supplierLedgerEntryId: $supplierLedgerEntryId, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class $ExpenseDetailDtoCopyWith<$Res>  {
  factory $ExpenseDetailDtoCopyWith(ExpenseDetailDto value, $Res Function(ExpenseDetailDto) _then) = _$ExpenseDetailDtoCopyWithImpl;
@useResult
$Res call({
 String id, String shopId, String categoryId, String categoryName, double amount, String paidTo, String? description, DateTime expenseDate, String actorUserId, bool isVoided, String? originalExpenseId, String? supplierLedgerEntryId, DateTime createdAt
});




}
/// @nodoc
class _$ExpenseDetailDtoCopyWithImpl<$Res>
    implements $ExpenseDetailDtoCopyWith<$Res> {
  _$ExpenseDetailDtoCopyWithImpl(this._self, this._then);

  final ExpenseDetailDto _self;
  final $Res Function(ExpenseDetailDto) _then;

/// Create a copy of ExpenseDetailDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? shopId = null,Object? categoryId = null,Object? categoryName = null,Object? amount = null,Object? paidTo = null,Object? description = freezed,Object? expenseDate = null,Object? actorUserId = null,Object? isVoided = null,Object? originalExpenseId = freezed,Object? supplierLedgerEntryId = freezed,Object? createdAt = null,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,categoryId: null == categoryId ? _self.categoryId : categoryId // ignore: cast_nullable_to_non_nullable
as String,categoryName: null == categoryName ? _self.categoryName : categoryName // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,paidTo: null == paidTo ? _self.paidTo : paidTo // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,expenseDate: null == expenseDate ? _self.expenseDate : expenseDate // ignore: cast_nullable_to_non_nullable
as DateTime,actorUserId: null == actorUserId ? _self.actorUserId : actorUserId // ignore: cast_nullable_to_non_nullable
as String,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,originalExpenseId: freezed == originalExpenseId ? _self.originalExpenseId : originalExpenseId // ignore: cast_nullable_to_non_nullable
as String?,supplierLedgerEntryId: freezed == supplierLedgerEntryId ? _self.supplierLedgerEntryId : supplierLedgerEntryId // ignore: cast_nullable_to_non_nullable
as String?,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [ExpenseDetailDto].
extension ExpenseDetailDtoPatterns on ExpenseDetailDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ExpenseDetailDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ExpenseDetailDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ExpenseDetailDto value)  $default,){
final _that = this;
switch (_that) {
case _ExpenseDetailDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ExpenseDetailDto value)?  $default,){
final _that = this;
switch (_that) {
case _ExpenseDetailDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String id,  String shopId,  String categoryId,  String categoryName,  double amount,  String paidTo,  String? description,  DateTime expenseDate,  String actorUserId,  bool isVoided,  String? originalExpenseId,  String? supplierLedgerEntryId,  DateTime createdAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ExpenseDetailDto() when $default != null:
return $default(_that.id,_that.shopId,_that.categoryId,_that.categoryName,_that.amount,_that.paidTo,_that.description,_that.expenseDate,_that.actorUserId,_that.isVoided,_that.originalExpenseId,_that.supplierLedgerEntryId,_that.createdAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String id,  String shopId,  String categoryId,  String categoryName,  double amount,  String paidTo,  String? description,  DateTime expenseDate,  String actorUserId,  bool isVoided,  String? originalExpenseId,  String? supplierLedgerEntryId,  DateTime createdAt)  $default,) {final _that = this;
switch (_that) {
case _ExpenseDetailDto():
return $default(_that.id,_that.shopId,_that.categoryId,_that.categoryName,_that.amount,_that.paidTo,_that.description,_that.expenseDate,_that.actorUserId,_that.isVoided,_that.originalExpenseId,_that.supplierLedgerEntryId,_that.createdAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String id,  String shopId,  String categoryId,  String categoryName,  double amount,  String paidTo,  String? description,  DateTime expenseDate,  String actorUserId,  bool isVoided,  String? originalExpenseId,  String? supplierLedgerEntryId,  DateTime createdAt)?  $default,) {final _that = this;
switch (_that) {
case _ExpenseDetailDto() when $default != null:
return $default(_that.id,_that.shopId,_that.categoryId,_that.categoryName,_that.amount,_that.paidTo,_that.description,_that.expenseDate,_that.actorUserId,_that.isVoided,_that.originalExpenseId,_that.supplierLedgerEntryId,_that.createdAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ExpenseDetailDto implements ExpenseDetailDto {
  const _ExpenseDetailDto({required this.id, required this.shopId, required this.categoryId, required this.categoryName, required this.amount, required this.paidTo, this.description, required this.expenseDate, required this.actorUserId, required this.isVoided, this.originalExpenseId, this.supplierLedgerEntryId, required this.createdAt});
  factory _ExpenseDetailDto.fromJson(Map<String, dynamic> json) => _$ExpenseDetailDtoFromJson(json);

@override final  String id;
@override final  String shopId;
@override final  String categoryId;
@override final  String categoryName;
@override final  double amount;
@override final  String paidTo;
@override final  String? description;
@override final  DateTime expenseDate;
@override final  String actorUserId;
@override final  bool isVoided;
@override final  String? originalExpenseId;
@override final  String? supplierLedgerEntryId;
@override final  DateTime createdAt;

/// Create a copy of ExpenseDetailDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ExpenseDetailDtoCopyWith<_ExpenseDetailDto> get copyWith => __$ExpenseDetailDtoCopyWithImpl<_ExpenseDetailDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ExpenseDetailDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ExpenseDetailDto&&(identical(other.id, id) || other.id == id)&&(identical(other.shopId, shopId) || other.shopId == shopId)&&(identical(other.categoryId, categoryId) || other.categoryId == categoryId)&&(identical(other.categoryName, categoryName) || other.categoryName == categoryName)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.paidTo, paidTo) || other.paidTo == paidTo)&&(identical(other.description, description) || other.description == description)&&(identical(other.expenseDate, expenseDate) || other.expenseDate == expenseDate)&&(identical(other.actorUserId, actorUserId) || other.actorUserId == actorUserId)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided)&&(identical(other.originalExpenseId, originalExpenseId) || other.originalExpenseId == originalExpenseId)&&(identical(other.supplierLedgerEntryId, supplierLedgerEntryId) || other.supplierLedgerEntryId == supplierLedgerEntryId)&&(identical(other.createdAt, createdAt) || other.createdAt == createdAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,shopId,categoryId,categoryName,amount,paidTo,description,expenseDate,actorUserId,isVoided,originalExpenseId,supplierLedgerEntryId,createdAt);

@override
String toString() {
  return 'ExpenseDetailDto(id: $id, shopId: $shopId, categoryId: $categoryId, categoryName: $categoryName, amount: $amount, paidTo: $paidTo, description: $description, expenseDate: $expenseDate, actorUserId: $actorUserId, isVoided: $isVoided, originalExpenseId: $originalExpenseId, supplierLedgerEntryId: $supplierLedgerEntryId, createdAt: $createdAt)';
}


}

/// @nodoc
abstract mixin class _$ExpenseDetailDtoCopyWith<$Res> implements $ExpenseDetailDtoCopyWith<$Res> {
  factory _$ExpenseDetailDtoCopyWith(_ExpenseDetailDto value, $Res Function(_ExpenseDetailDto) _then) = __$ExpenseDetailDtoCopyWithImpl;
@override @useResult
$Res call({
 String id, String shopId, String categoryId, String categoryName, double amount, String paidTo, String? description, DateTime expenseDate, String actorUserId, bool isVoided, String? originalExpenseId, String? supplierLedgerEntryId, DateTime createdAt
});




}
/// @nodoc
class __$ExpenseDetailDtoCopyWithImpl<$Res>
    implements _$ExpenseDetailDtoCopyWith<$Res> {
  __$ExpenseDetailDtoCopyWithImpl(this._self, this._then);

  final _ExpenseDetailDto _self;
  final $Res Function(_ExpenseDetailDto) _then;

/// Create a copy of ExpenseDetailDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? shopId = null,Object? categoryId = null,Object? categoryName = null,Object? amount = null,Object? paidTo = null,Object? description = freezed,Object? expenseDate = null,Object? actorUserId = null,Object? isVoided = null,Object? originalExpenseId = freezed,Object? supplierLedgerEntryId = freezed,Object? createdAt = null,}) {
  return _then(_ExpenseDetailDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,shopId: null == shopId ? _self.shopId : shopId // ignore: cast_nullable_to_non_nullable
as String,categoryId: null == categoryId ? _self.categoryId : categoryId // ignore: cast_nullable_to_non_nullable
as String,categoryName: null == categoryName ? _self.categoryName : categoryName // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,paidTo: null == paidTo ? _self.paidTo : paidTo // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,expenseDate: null == expenseDate ? _self.expenseDate : expenseDate // ignore: cast_nullable_to_non_nullable
as DateTime,actorUserId: null == actorUserId ? _self.actorUserId : actorUserId // ignore: cast_nullable_to_non_nullable
as String,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,originalExpenseId: freezed == originalExpenseId ? _self.originalExpenseId : originalExpenseId // ignore: cast_nullable_to_non_nullable
as String?,supplierLedgerEntryId: freezed == supplierLedgerEntryId ? _self.supplierLedgerEntryId : supplierLedgerEntryId // ignore: cast_nullable_to_non_nullable
as String?,createdAt: null == createdAt ? _self.createdAt : createdAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}

// dart format on
