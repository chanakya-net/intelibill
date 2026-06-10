// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'sales_history_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$SalesHistorySummaryDto {

@JsonKey(name: 'periodSales') double get periodSales;@JsonKey(name: 'invoiceCount') int get invoiceCount;@JsonKey(name: 'refundAmount') double get refundAmount;
/// Create a copy of SalesHistorySummaryDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SalesHistorySummaryDtoCopyWith<SalesHistorySummaryDto> get copyWith => _$SalesHistorySummaryDtoCopyWithImpl<SalesHistorySummaryDto>(this as SalesHistorySummaryDto, _$identity);

  /// Serializes this SalesHistorySummaryDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SalesHistorySummaryDto&&(identical(other.periodSales, periodSales) || other.periodSales == periodSales)&&(identical(other.invoiceCount, invoiceCount) || other.invoiceCount == invoiceCount)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,periodSales,invoiceCount,refundAmount);

@override
String toString() {
  return 'SalesHistorySummaryDto(periodSales: $periodSales, invoiceCount: $invoiceCount, refundAmount: $refundAmount)';
}


}

/// @nodoc
abstract mixin class $SalesHistorySummaryDtoCopyWith<$Res>  {
  factory $SalesHistorySummaryDtoCopyWith(SalesHistorySummaryDto value, $Res Function(SalesHistorySummaryDto) _then) = _$SalesHistorySummaryDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'periodSales') double periodSales,@JsonKey(name: 'invoiceCount') int invoiceCount,@JsonKey(name: 'refundAmount') double refundAmount
});




}
/// @nodoc
class _$SalesHistorySummaryDtoCopyWithImpl<$Res>
    implements $SalesHistorySummaryDtoCopyWith<$Res> {
  _$SalesHistorySummaryDtoCopyWithImpl(this._self, this._then);

  final SalesHistorySummaryDto _self;
  final $Res Function(SalesHistorySummaryDto) _then;

/// Create a copy of SalesHistorySummaryDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? periodSales = null,Object? invoiceCount = null,Object? refundAmount = null,}) {
  return _then(_self.copyWith(
periodSales: null == periodSales ? _self.periodSales : periodSales // ignore: cast_nullable_to_non_nullable
as double,invoiceCount: null == invoiceCount ? _self.invoiceCount : invoiceCount // ignore: cast_nullable_to_non_nullable
as int,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}

}


/// Adds pattern-matching-related methods to [SalesHistorySummaryDto].
extension SalesHistorySummaryDtoPatterns on SalesHistorySummaryDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SalesHistorySummaryDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SalesHistorySummaryDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SalesHistorySummaryDto value)  $default,){
final _that = this;
switch (_that) {
case _SalesHistorySummaryDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SalesHistorySummaryDto value)?  $default,){
final _that = this;
switch (_that) {
case _SalesHistorySummaryDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'periodSales')  double periodSales, @JsonKey(name: 'invoiceCount')  int invoiceCount, @JsonKey(name: 'refundAmount')  double refundAmount)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SalesHistorySummaryDto() when $default != null:
return $default(_that.periodSales,_that.invoiceCount,_that.refundAmount);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'periodSales')  double periodSales, @JsonKey(name: 'invoiceCount')  int invoiceCount, @JsonKey(name: 'refundAmount')  double refundAmount)  $default,) {final _that = this;
switch (_that) {
case _SalesHistorySummaryDto():
return $default(_that.periodSales,_that.invoiceCount,_that.refundAmount);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'periodSales')  double periodSales, @JsonKey(name: 'invoiceCount')  int invoiceCount, @JsonKey(name: 'refundAmount')  double refundAmount)?  $default,) {final _that = this;
switch (_that) {
case _SalesHistorySummaryDto() when $default != null:
return $default(_that.periodSales,_that.invoiceCount,_that.refundAmount);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SalesHistorySummaryDto implements SalesHistorySummaryDto {
  const _SalesHistorySummaryDto({@JsonKey(name: 'periodSales') required this.periodSales, @JsonKey(name: 'invoiceCount') required this.invoiceCount, @JsonKey(name: 'refundAmount') required this.refundAmount});
  factory _SalesHistorySummaryDto.fromJson(Map<String, dynamic> json) => _$SalesHistorySummaryDtoFromJson(json);

@override@JsonKey(name: 'periodSales') final  double periodSales;
@override@JsonKey(name: 'invoiceCount') final  int invoiceCount;
@override@JsonKey(name: 'refundAmount') final  double refundAmount;

/// Create a copy of SalesHistorySummaryDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SalesHistorySummaryDtoCopyWith<_SalesHistorySummaryDto> get copyWith => __$SalesHistorySummaryDtoCopyWithImpl<_SalesHistorySummaryDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SalesHistorySummaryDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SalesHistorySummaryDto&&(identical(other.periodSales, periodSales) || other.periodSales == periodSales)&&(identical(other.invoiceCount, invoiceCount) || other.invoiceCount == invoiceCount)&&(identical(other.refundAmount, refundAmount) || other.refundAmount == refundAmount));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,periodSales,invoiceCount,refundAmount);

@override
String toString() {
  return 'SalesHistorySummaryDto(periodSales: $periodSales, invoiceCount: $invoiceCount, refundAmount: $refundAmount)';
}


}

/// @nodoc
abstract mixin class _$SalesHistorySummaryDtoCopyWith<$Res> implements $SalesHistorySummaryDtoCopyWith<$Res> {
  factory _$SalesHistorySummaryDtoCopyWith(_SalesHistorySummaryDto value, $Res Function(_SalesHistorySummaryDto) _then) = __$SalesHistorySummaryDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'periodSales') double periodSales,@JsonKey(name: 'invoiceCount') int invoiceCount,@JsonKey(name: 'refundAmount') double refundAmount
});




}
/// @nodoc
class __$SalesHistorySummaryDtoCopyWithImpl<$Res>
    implements _$SalesHistorySummaryDtoCopyWith<$Res> {
  __$SalesHistorySummaryDtoCopyWithImpl(this._self, this._then);

  final _SalesHistorySummaryDto _self;
  final $Res Function(_SalesHistorySummaryDto) _then;

/// Create a copy of SalesHistorySummaryDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? periodSales = null,Object? invoiceCount = null,Object? refundAmount = null,}) {
  return _then(_SalesHistorySummaryDto(
periodSales: null == periodSales ? _self.periodSales : periodSales // ignore: cast_nullable_to_non_nullable
as double,invoiceCount: null == invoiceCount ? _self.invoiceCount : invoiceCount // ignore: cast_nullable_to_non_nullable
as int,refundAmount: null == refundAmount ? _self.refundAmount : refundAmount // ignore: cast_nullable_to_non_nullable
as double,
  ));
}


}


/// @nodoc
mixin _$SalesHistoryResponseDto {

@JsonKey(name: 'items') List<SaleListItemDto> get items;@JsonKey(name: 'totalCount') int get totalCount;@JsonKey(name: 'pageNumber') int get pageNumber;@JsonKey(name: 'pageSize') int get pageSize;@JsonKey(name: 'summary') SalesHistorySummaryDto get summary;
/// Create a copy of SalesHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$SalesHistoryResponseDtoCopyWith<SalesHistoryResponseDto> get copyWith => _$SalesHistoryResponseDtoCopyWithImpl<SalesHistoryResponseDto>(this as SalesHistoryResponseDto, _$identity);

  /// Serializes this SalesHistoryResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is SalesHistoryResponseDto&&const DeepCollectionEquality().equals(other.items, items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize)&&(identical(other.summary, summary) || other.summary == summary));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(items),totalCount,pageNumber,pageSize,summary);

@override
String toString() {
  return 'SalesHistoryResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize, summary: $summary)';
}


}

/// @nodoc
abstract mixin class $SalesHistoryResponseDtoCopyWith<$Res>  {
  factory $SalesHistoryResponseDtoCopyWith(SalesHistoryResponseDto value, $Res Function(SalesHistoryResponseDto) _then) = _$SalesHistoryResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'items') List<SaleListItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize,@JsonKey(name: 'summary') SalesHistorySummaryDto summary
});


$SalesHistorySummaryDtoCopyWith<$Res> get summary;

}
/// @nodoc
class _$SalesHistoryResponseDtoCopyWithImpl<$Res>
    implements $SalesHistoryResponseDtoCopyWith<$Res> {
  _$SalesHistoryResponseDtoCopyWithImpl(this._self, this._then);

  final SalesHistoryResponseDto _self;
  final $Res Function(SalesHistoryResponseDto) _then;

/// Create a copy of SalesHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,Object? summary = null,}) {
  return _then(_self.copyWith(
items: null == items ? _self.items : items // ignore: cast_nullable_to_non_nullable
as List<SaleListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,summary: null == summary ? _self.summary : summary // ignore: cast_nullable_to_non_nullable
as SalesHistorySummaryDto,
  ));
}
/// Create a copy of SalesHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SalesHistorySummaryDtoCopyWith<$Res> get summary {
  
  return $SalesHistorySummaryDtoCopyWith<$Res>(_self.summary, (value) {
    return _then(_self.copyWith(summary: value));
  });
}
}


/// Adds pattern-matching-related methods to [SalesHistoryResponseDto].
extension SalesHistoryResponseDtoPatterns on SalesHistoryResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _SalesHistoryResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _SalesHistoryResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _SalesHistoryResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _SalesHistoryResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _SalesHistoryResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _SalesHistoryResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<SaleListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize, @JsonKey(name: 'summary')  SalesHistorySummaryDto summary)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _SalesHistoryResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize,_that.summary);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'items')  List<SaleListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize, @JsonKey(name: 'summary')  SalesHistorySummaryDto summary)  $default,) {final _that = this;
switch (_that) {
case _SalesHistoryResponseDto():
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize,_that.summary);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'items')  List<SaleListItemDto> items, @JsonKey(name: 'totalCount')  int totalCount, @JsonKey(name: 'pageNumber')  int pageNumber, @JsonKey(name: 'pageSize')  int pageSize, @JsonKey(name: 'summary')  SalesHistorySummaryDto summary)?  $default,) {final _that = this;
switch (_that) {
case _SalesHistoryResponseDto() when $default != null:
return $default(_that.items,_that.totalCount,_that.pageNumber,_that.pageSize,_that.summary);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _SalesHistoryResponseDto implements SalesHistoryResponseDto {
  const _SalesHistoryResponseDto({@JsonKey(name: 'items') final  List<SaleListItemDto> items = const [], @JsonKey(name: 'totalCount') required this.totalCount, @JsonKey(name: 'pageNumber') required this.pageNumber, @JsonKey(name: 'pageSize') required this.pageSize, @JsonKey(name: 'summary') required this.summary}): _items = items;
  factory _SalesHistoryResponseDto.fromJson(Map<String, dynamic> json) => _$SalesHistoryResponseDtoFromJson(json);

 final  List<SaleListItemDto> _items;
@override@JsonKey(name: 'items') List<SaleListItemDto> get items {
  if (_items is EqualUnmodifiableListView) return _items;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_items);
}

@override@JsonKey(name: 'totalCount') final  int totalCount;
@override@JsonKey(name: 'pageNumber') final  int pageNumber;
@override@JsonKey(name: 'pageSize') final  int pageSize;
@override@JsonKey(name: 'summary') final  SalesHistorySummaryDto summary;

/// Create a copy of SalesHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$SalesHistoryResponseDtoCopyWith<_SalesHistoryResponseDto> get copyWith => __$SalesHistoryResponseDtoCopyWithImpl<_SalesHistoryResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$SalesHistoryResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _SalesHistoryResponseDto&&const DeepCollectionEquality().equals(other._items, _items)&&(identical(other.totalCount, totalCount) || other.totalCount == totalCount)&&(identical(other.pageNumber, pageNumber) || other.pageNumber == pageNumber)&&(identical(other.pageSize, pageSize) || other.pageSize == pageSize)&&(identical(other.summary, summary) || other.summary == summary));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,const DeepCollectionEquality().hash(_items),totalCount,pageNumber,pageSize,summary);

@override
String toString() {
  return 'SalesHistoryResponseDto(items: $items, totalCount: $totalCount, pageNumber: $pageNumber, pageSize: $pageSize, summary: $summary)';
}


}

/// @nodoc
abstract mixin class _$SalesHistoryResponseDtoCopyWith<$Res> implements $SalesHistoryResponseDtoCopyWith<$Res> {
  factory _$SalesHistoryResponseDtoCopyWith(_SalesHistoryResponseDto value, $Res Function(_SalesHistoryResponseDto) _then) = __$SalesHistoryResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'items') List<SaleListItemDto> items,@JsonKey(name: 'totalCount') int totalCount,@JsonKey(name: 'pageNumber') int pageNumber,@JsonKey(name: 'pageSize') int pageSize,@JsonKey(name: 'summary') SalesHistorySummaryDto summary
});


@override $SalesHistorySummaryDtoCopyWith<$Res> get summary;

}
/// @nodoc
class __$SalesHistoryResponseDtoCopyWithImpl<$Res>
    implements _$SalesHistoryResponseDtoCopyWith<$Res> {
  __$SalesHistoryResponseDtoCopyWithImpl(this._self, this._then);

  final _SalesHistoryResponseDto _self;
  final $Res Function(_SalesHistoryResponseDto) _then;

/// Create a copy of SalesHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? items = null,Object? totalCount = null,Object? pageNumber = null,Object? pageSize = null,Object? summary = null,}) {
  return _then(_SalesHistoryResponseDto(
items: null == items ? _self._items : items // ignore: cast_nullable_to_non_nullable
as List<SaleListItemDto>,totalCount: null == totalCount ? _self.totalCount : totalCount // ignore: cast_nullable_to_non_nullable
as int,pageNumber: null == pageNumber ? _self.pageNumber : pageNumber // ignore: cast_nullable_to_non_nullable
as int,pageSize: null == pageSize ? _self.pageSize : pageSize // ignore: cast_nullable_to_non_nullable
as int,summary: null == summary ? _self.summary : summary // ignore: cast_nullable_to_non_nullable
as SalesHistorySummaryDto,
  ));
}

/// Create a copy of SalesHistoryResponseDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$SalesHistorySummaryDtoCopyWith<$Res> get summary {
  
  return $SalesHistorySummaryDtoCopyWith<$Res>(_self.summary, (value) {
    return _then(_self.copyWith(summary: value));
  });
}
}

// dart format on
