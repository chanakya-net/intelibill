// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'expense_mutation_request_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$ExpenseMutationRequestDto {

@JsonKey(toJson: _trim) String get categoryName; double get amount;@JsonKey(toJson: _trim) String get paidTo;@JsonKey(toJson: _trimNullable) String? get description;@JsonKey(toJson: _dateOnlyToJson) DateTime get expenseDate;
/// Create a copy of ExpenseMutationRequestDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$ExpenseMutationRequestDtoCopyWith<ExpenseMutationRequestDto> get copyWith => _$ExpenseMutationRequestDtoCopyWithImpl<ExpenseMutationRequestDto>(this as ExpenseMutationRequestDto, _$identity);

  /// Serializes this ExpenseMutationRequestDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is ExpenseMutationRequestDto&&(identical(other.categoryName, categoryName) || other.categoryName == categoryName)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.paidTo, paidTo) || other.paidTo == paidTo)&&(identical(other.description, description) || other.description == description)&&(identical(other.expenseDate, expenseDate) || other.expenseDate == expenseDate));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,categoryName,amount,paidTo,description,expenseDate);

@override
String toString() {
  return 'ExpenseMutationRequestDto(categoryName: $categoryName, amount: $amount, paidTo: $paidTo, description: $description, expenseDate: $expenseDate)';
}


}

/// @nodoc
abstract mixin class $ExpenseMutationRequestDtoCopyWith<$Res>  {
  factory $ExpenseMutationRequestDtoCopyWith(ExpenseMutationRequestDto value, $Res Function(ExpenseMutationRequestDto) _then) = _$ExpenseMutationRequestDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(toJson: _trim) String categoryName, double amount,@JsonKey(toJson: _trim) String paidTo,@JsonKey(toJson: _trimNullable) String? description,@JsonKey(toJson: _dateOnlyToJson) DateTime expenseDate
});




}
/// @nodoc
class _$ExpenseMutationRequestDtoCopyWithImpl<$Res>
    implements $ExpenseMutationRequestDtoCopyWith<$Res> {
  _$ExpenseMutationRequestDtoCopyWithImpl(this._self, this._then);

  final ExpenseMutationRequestDto _self;
  final $Res Function(ExpenseMutationRequestDto) _then;

/// Create a copy of ExpenseMutationRequestDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? categoryName = null,Object? amount = null,Object? paidTo = null,Object? description = freezed,Object? expenseDate = null,}) {
  return _then(_self.copyWith(
categoryName: null == categoryName ? _self.categoryName : categoryName // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,paidTo: null == paidTo ? _self.paidTo : paidTo // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,expenseDate: null == expenseDate ? _self.expenseDate : expenseDate // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [ExpenseMutationRequestDto].
extension ExpenseMutationRequestDtoPatterns on ExpenseMutationRequestDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _ExpenseMutationRequestDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _ExpenseMutationRequestDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _ExpenseMutationRequestDto value)  $default,){
final _that = this;
switch (_that) {
case _ExpenseMutationRequestDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _ExpenseMutationRequestDto value)?  $default,){
final _that = this;
switch (_that) {
case _ExpenseMutationRequestDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(toJson: _trim)  String categoryName,  double amount, @JsonKey(toJson: _trim)  String paidTo, @JsonKey(toJson: _trimNullable)  String? description, @JsonKey(toJson: _dateOnlyToJson)  DateTime expenseDate)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _ExpenseMutationRequestDto() when $default != null:
return $default(_that.categoryName,_that.amount,_that.paidTo,_that.description,_that.expenseDate);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(toJson: _trim)  String categoryName,  double amount, @JsonKey(toJson: _trim)  String paidTo, @JsonKey(toJson: _trimNullable)  String? description, @JsonKey(toJson: _dateOnlyToJson)  DateTime expenseDate)  $default,) {final _that = this;
switch (_that) {
case _ExpenseMutationRequestDto():
return $default(_that.categoryName,_that.amount,_that.paidTo,_that.description,_that.expenseDate);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(toJson: _trim)  String categoryName,  double amount, @JsonKey(toJson: _trim)  String paidTo, @JsonKey(toJson: _trimNullable)  String? description, @JsonKey(toJson: _dateOnlyToJson)  DateTime expenseDate)?  $default,) {final _that = this;
switch (_that) {
case _ExpenseMutationRequestDto() when $default != null:
return $default(_that.categoryName,_that.amount,_that.paidTo,_that.description,_that.expenseDate);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _ExpenseMutationRequestDto implements ExpenseMutationRequestDto {
  const _ExpenseMutationRequestDto({@JsonKey(toJson: _trim) required this.categoryName, required this.amount, @JsonKey(toJson: _trim) required this.paidTo, @JsonKey(toJson: _trimNullable) this.description, @JsonKey(toJson: _dateOnlyToJson) required this.expenseDate});
  factory _ExpenseMutationRequestDto.fromJson(Map<String, dynamic> json) => _$ExpenseMutationRequestDtoFromJson(json);

@override@JsonKey(toJson: _trim) final  String categoryName;
@override final  double amount;
@override@JsonKey(toJson: _trim) final  String paidTo;
@override@JsonKey(toJson: _trimNullable) final  String? description;
@override@JsonKey(toJson: _dateOnlyToJson) final  DateTime expenseDate;

/// Create a copy of ExpenseMutationRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$ExpenseMutationRequestDtoCopyWith<_ExpenseMutationRequestDto> get copyWith => __$ExpenseMutationRequestDtoCopyWithImpl<_ExpenseMutationRequestDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$ExpenseMutationRequestDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _ExpenseMutationRequestDto&&(identical(other.categoryName, categoryName) || other.categoryName == categoryName)&&(identical(other.amount, amount) || other.amount == amount)&&(identical(other.paidTo, paidTo) || other.paidTo == paidTo)&&(identical(other.description, description) || other.description == description)&&(identical(other.expenseDate, expenseDate) || other.expenseDate == expenseDate));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,categoryName,amount,paidTo,description,expenseDate);

@override
String toString() {
  return 'ExpenseMutationRequestDto(categoryName: $categoryName, amount: $amount, paidTo: $paidTo, description: $description, expenseDate: $expenseDate)';
}


}

/// @nodoc
abstract mixin class _$ExpenseMutationRequestDtoCopyWith<$Res> implements $ExpenseMutationRequestDtoCopyWith<$Res> {
  factory _$ExpenseMutationRequestDtoCopyWith(_ExpenseMutationRequestDto value, $Res Function(_ExpenseMutationRequestDto) _then) = __$ExpenseMutationRequestDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(toJson: _trim) String categoryName, double amount,@JsonKey(toJson: _trim) String paidTo,@JsonKey(toJson: _trimNullable) String? description,@JsonKey(toJson: _dateOnlyToJson) DateTime expenseDate
});




}
/// @nodoc
class __$ExpenseMutationRequestDtoCopyWithImpl<$Res>
    implements _$ExpenseMutationRequestDtoCopyWith<$Res> {
  __$ExpenseMutationRequestDtoCopyWithImpl(this._self, this._then);

  final _ExpenseMutationRequestDto _self;
  final $Res Function(_ExpenseMutationRequestDto) _then;

/// Create a copy of ExpenseMutationRequestDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? categoryName = null,Object? amount = null,Object? paidTo = null,Object? description = freezed,Object? expenseDate = null,}) {
  return _then(_ExpenseMutationRequestDto(
categoryName: null == categoryName ? _self.categoryName : categoryName // ignore: cast_nullable_to_non_nullable
as String,amount: null == amount ? _self.amount : amount // ignore: cast_nullable_to_non_nullable
as double,paidTo: null == paidTo ? _self.paidTo : paidTo // ignore: cast_nullable_to_non_nullable
as String,description: freezed == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String?,expenseDate: null == expenseDate ? _self.expenseDate : expenseDate // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}

// dart format on
