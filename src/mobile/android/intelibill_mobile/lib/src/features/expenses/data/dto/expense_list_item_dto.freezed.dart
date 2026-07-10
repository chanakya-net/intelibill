// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'expense_list_item_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ExpenseListItemDto {

 String get id; double get amount; String get categoryName; String get paidTo; DateTime get expenseDate; bool get isVoided;
/// Create a copy of ExpenseListItemDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ExpenseListItemDtoCopyWith<ExpenseListItemDto> get copyWith => _$ExpenseListItemDtoCopyWithImpl<ExpenseListItemDto>(this as ExpenseListItemDto, _$identity);

  /// Serializes this ExpenseListItemDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ExpenseListItemDto&&(identical(other.id, id) || other.id == id)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.categoryName, categoryName) || other.categoryName == categoryName)&&(identical(other.paidTo, paidTo) || other.paidTo == paidTo)&&(identical(other.expenseDate, expenseDate) || other.expenseDate == expenseDate)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,amount,categoryName,paidTo,expenseDate,isVoided);

@override
String toString() {
  return 'ExpenseListItemDto(id: $id, amount: $amount, categoryName: $categoryName, paidTo: $paidTo, expenseDate: $expenseDate, isVoided: $isVoided)';
}


}

/// @nodoc
abstract mixin class $ExpenseListItemDtoCopyWith<$Res>  {
  factory $ExpenseListItemDtoCopyWith(ExpenseListItemDto value, $Res Function(ExpenseListItemDto) _then) = _$ExpenseListItemDtoCopyWithImpl;
@useResult
$Res call({
 String id, double amount, String categoryName, String paidTo, DateTime expenseDate, bool isVoided
});




}
/// @nodoc
class _$ExpenseListItemDtoCopyWithImpl<$Res>
    implements $ExpenseListItemDtoCopyWith<$Res> {
  _$ExpenseListItemDtoCopyWithImpl(this._self, this._then);

  final ExpenseListItemDto _self;
  final $Res Function(ExpenseListItemDto) _then;

/// Create a copy of ExpenseListItemDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? id = null,Object? amount = null,Object? categoryName = null,Object? paidTo = null,Object? expenseDate = null,Object? isVoided = null,}) {
  return _then(_self.copyWith(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,categoryName: null == categoryName ? _self.categoryName : categoryName // ignore: cast_nullable_to_non_nullable
as String,paidTo: null == paidTo ? _self.paidTo : paidTo // ignore: cast_nullable_to_non_nullable
as String,expenseDate: null == expenseDate ? _self.expenseDate : expenseDate // ignore: cast_nullable_to_non_nullable
as DateTime,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}

}


/// Adds pattern-matching-related methods to [ExpenseListItemDto].
extension ExpenseListItemDtoPatterns on ExpenseListItemDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ExpenseListItemDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ExpenseListItemDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ExpenseListItemDto value)  $default,){
final _that = this;
switch (_that) {
case _ExpenseListItemDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ExpenseListItemDto value)?  $default,){
final _that = this;
switch (_that) {
case _ExpenseListItemDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function( String id,  double amount,  String categoryName,  String paidTo,  DateTime expenseDate,  bool isVoided)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ExpenseListItemDto() when $default != null:
return $default(_that.id,_that.amount,_that.categoryName,_that.paidTo,_that.expenseDate,_that.isVoided);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function( String id,  double amount,  String categoryName,  String paidTo,  DateTime expenseDate,  bool isVoided)  $default,) {final _that = this;
switch (_that) {
case _ExpenseListItemDto():
return $default(_that.id,_that.amount,_that.categoryName,_that.paidTo,_that.expenseDate,_that.isVoided);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function( String id,  double amount,  String categoryName,  String paidTo,  DateTime expenseDate,  bool isVoided)?  $default,) {final _that = this;
switch (_that) {
case _ExpenseListItemDto() when $default != null:
return $default(_that.id,_that.amount,_that.categoryName,_that.paidTo,_that.expenseDate,_that.isVoided);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ExpenseListItemDto implements ExpenseListItemDto {
  const _ExpenseListItemDto({required this.id, required this.amount, required this.categoryName, required this.paidTo, required this.expenseDate, required this.isVoided});
  factory _ExpenseListItemDto.fromJson(Map<String, dynamic> json) => _$ExpenseListItemDtoFromJson(json);

@override final  String id;
@override final  double amount;
@override final  String categoryName;
@override final  String paidTo;
@override final  DateTime expenseDate;
@override final  bool isVoided;

/// Create a copy of ExpenseListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ExpenseListItemDtoCopyWith<_ExpenseListItemDto> get copyWith => __$ExpenseListItemDtoCopyWithImpl<_ExpenseListItemDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ExpenseListItemDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ExpenseListItemDto&&(identical(other.id, id) || other.id == id)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.categoryName, categoryName) || other.categoryName == categoryName)&&(identical(other.paidTo, paidTo) || other.paidTo == paidTo)&&(identical(other.expenseDate, expenseDate) || other.expenseDate == expenseDate)&&(identical(other.isVoided, isVoided) || other.isVoided == isVoided));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,id,amount,categoryName,paidTo,expenseDate,isVoided);

@override
String toString() {
  return 'ExpenseListItemDto(id: $id, amount: $amount, categoryName: $categoryName, paidTo: $paidTo, expenseDate: $expenseDate, isVoided: $isVoided)';
}


}

/// @nodoc
abstract mixin class _$ExpenseListItemDtoCopyWith<$Res> implements $ExpenseListItemDtoCopyWith<$Res> {
  factory _$ExpenseListItemDtoCopyWith(_ExpenseListItemDto value, $Res Function(_ExpenseListItemDto) _then) = __$ExpenseListItemDtoCopyWithImpl;
@override @useResult
$Res call({
 String id, double amount, String categoryName, String paidTo, DateTime expenseDate, bool isVoided
});




}
/// @nodoc
class __$ExpenseListItemDtoCopyWithImpl<$Res>
    implements _$ExpenseListItemDtoCopyWith<$Res> {
  __$ExpenseListItemDtoCopyWithImpl(this._self, this._then);

  final _ExpenseListItemDto _self;
  final $Res Function(_ExpenseListItemDto) _then;

/// Create a copy of ExpenseListItemDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? id = null,Object? amount = null,Object? categoryName = null,Object? paidTo = null,Object? expenseDate = null,Object? isVoided = null,}) {
  return _then(_ExpenseListItemDto(
id: null == id ? _self.id : id // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,categoryName: null == categoryName ? _self.categoryName : categoryName // ignore: cast_nullable_to_non_nullable
as String,paidTo: null == paidTo ? _self.paidTo : paidTo // ignore: cast_nullable_to_non_nullable
as String,expenseDate: null == expenseDate ? _self.expenseDate : expenseDate // ignore: cast_nullable_to_non_nullable
as DateTime,isVoided: null == isVoided ? _self.isVoided : isVoided // ignore: cast_nullable_to_non_nullable
as bool,
  ));
}


}

// dart format on
