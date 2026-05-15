// GENERATED CODE - DO NOT MODIFY BY HAND
// coverage:ignore-file
// ignore_for_file: type=lint
// ignore_for_file: unused_element, deprecated_member_use, deprecated_member_use_from_same_package, use_function_type_syntax_for_parameters, unnecessary_const, avoid_init_to_null, invalid_override_different_default_values_named, prefer_expression_function_bodies, annotate_overrides, invalid_annotation_target, unnecessary_question_mark

part of 'add_inventory_batch_response_dto.dart';

// **************************************************************************
// FreezedGenerator
// **************************************************************************

// dart format off
T _$identity<T>(T value) => value;

/// @nodoc
mixin _$AddInventoryBatchResponseDto {

@JsonKey(name: 'requestedCount') int get requestedCount;@JsonKey(name: 'successCount') int get successCount;@JsonKey(name: 'failedCount') int get failedCount;@JsonKey(name: 'succeeded') List<AddInventoryBatchSucceededRowDto> get succeeded;@JsonKey(name: 'failed') List<AddInventoryBatchFailedRowDto> get failed;
/// Create a copy of AddInventoryBatchResponseDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryBatchResponseDtoCopyWith<AddInventoryBatchResponseDto> get copyWith => _$AddInventoryBatchResponseDtoCopyWithImpl<AddInventoryBatchResponseDto>(this as AddInventoryBatchResponseDto, _$identity);

  /// Serializes this AddInventoryBatchResponseDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryBatchResponseDto&&(identical(other.requestedCount, requestedCount) || other.requestedCount == requestedCount)&&(identical(other.successCount, successCount) || other.successCount == successCount)&&(identical(other.failedCount, failedCount) || other.failedCount == failedCount)&&const DeepCollectionEquality().equals(other.succeeded, succeeded)&&const DeepCollectionEquality().equals(other.failed, failed));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,requestedCount,successCount,failedCount,const DeepCollectionEquality().hash(succeeded),const DeepCollectionEquality().hash(failed));

@override
String toString() {
  return 'AddInventoryBatchResponseDto(requestedCount: $requestedCount, successCount: $successCount, failedCount: $failedCount, succeeded: $succeeded, failed: $failed)';
}


}

/// @nodoc
abstract mixin class $AddInventoryBatchResponseDtoCopyWith<$Res>  {
  factory $AddInventoryBatchResponseDtoCopyWith(AddInventoryBatchResponseDto value, $Res Function(AddInventoryBatchResponseDto) _then) = _$AddInventoryBatchResponseDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'requestedCount') int requestedCount,@JsonKey(name: 'successCount') int successCount,@JsonKey(name: 'failedCount') int failedCount,@JsonKey(name: 'succeeded') List<AddInventoryBatchSucceededRowDto> succeeded,@JsonKey(name: 'failed') List<AddInventoryBatchFailedRowDto> failed
});




}
/// @nodoc
class _$AddInventoryBatchResponseDtoCopyWithImpl<$Res>
    implements $AddInventoryBatchResponseDtoCopyWith<$Res> {
  _$AddInventoryBatchResponseDtoCopyWithImpl(this._self, this._then);

  final AddInventoryBatchResponseDto _self;
  final $Res Function(AddInventoryBatchResponseDto) _then;

/// Create a copy of AddInventoryBatchResponseDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? requestedCount = null,Object? successCount = null,Object? failedCount = null,Object? succeeded = null,Object? failed = null,}) {
  return _then(_self.copyWith(
requestedCount: null == requestedCount ? _self.requestedCount : requestedCount // ignore: cast_nullable_to_non_nullable
as int,successCount: null == successCount ? _self.successCount : successCount // ignore: cast_nullable_to_non_nullable
as int,failedCount: null == failedCount ? _self.failedCount : failedCount // ignore: cast_nullable_to_non_nullable
as int,succeeded: null == succeeded ? _self.succeeded : succeeded // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchSucceededRowDto>,failed: null == failed ? _self.failed : failed // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchFailedRowDto>,
  ));
}

}


/// Adds pattern-matching-related methods to [AddInventoryBatchResponseDto].
extension AddInventoryBatchResponseDtoPatterns on AddInventoryBatchResponseDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryBatchResponseDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryBatchResponseDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryBatchResponseDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchResponseDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryBatchResponseDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchResponseDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'requestedCount')  int requestedCount, @JsonKey(name: 'successCount')  int successCount, @JsonKey(name: 'failedCount')  int failedCount, @JsonKey(name: 'succeeded')  List<AddInventoryBatchSucceededRowDto> succeeded, @JsonKey(name: 'failed')  List<AddInventoryBatchFailedRowDto> failed)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryBatchResponseDto() when $default != null:
return $default(_that.requestedCount,_that.successCount,_that.failedCount,_that.succeeded,_that.failed);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'requestedCount')  int requestedCount, @JsonKey(name: 'successCount')  int successCount, @JsonKey(name: 'failedCount')  int failedCount, @JsonKey(name: 'succeeded')  List<AddInventoryBatchSucceededRowDto> succeeded, @JsonKey(name: 'failed')  List<AddInventoryBatchFailedRowDto> failed)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchResponseDto():
return $default(_that.requestedCount,_that.successCount,_that.failedCount,_that.succeeded,_that.failed);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'requestedCount')  int requestedCount, @JsonKey(name: 'successCount')  int successCount, @JsonKey(name: 'failedCount')  int failedCount, @JsonKey(name: 'succeeded')  List<AddInventoryBatchSucceededRowDto> succeeded, @JsonKey(name: 'failed')  List<AddInventoryBatchFailedRowDto> failed)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchResponseDto() when $default != null:
return $default(_that.requestedCount,_that.successCount,_that.failedCount,_that.succeeded,_that.failed);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryBatchResponseDto implements AddInventoryBatchResponseDto {
  const _AddInventoryBatchResponseDto({@JsonKey(name: 'requestedCount') required this.requestedCount, @JsonKey(name: 'successCount') required this.successCount, @JsonKey(name: 'failedCount') required this.failedCount, @JsonKey(name: 'succeeded') final  List<AddInventoryBatchSucceededRowDto> succeeded = const [], @JsonKey(name: 'failed') final  List<AddInventoryBatchFailedRowDto> failed = const []}): _succeeded = succeeded,_failed = failed;
  factory _AddInventoryBatchResponseDto.fromJson(Map<String, dynamic> json) => _$AddInventoryBatchResponseDtoFromJson(json);

@override@JsonKey(name: 'requestedCount') final  int requestedCount;
@override@JsonKey(name: 'successCount') final  int successCount;
@override@JsonKey(name: 'failedCount') final  int failedCount;
 final  List<AddInventoryBatchSucceededRowDto> _succeeded;
@override@JsonKey(name: 'succeeded') List<AddInventoryBatchSucceededRowDto> get succeeded {
  if (_succeeded is EqualUnmodifiableListView) return _succeeded;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_succeeded);
}

 final  List<AddInventoryBatchFailedRowDto> _failed;
@override@JsonKey(name: 'failed') List<AddInventoryBatchFailedRowDto> get failed {
  if (_failed is EqualUnmodifiableListView) return _failed;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_failed);
}


/// Create a copy of AddInventoryBatchResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryBatchResponseDtoCopyWith<_AddInventoryBatchResponseDto> get copyWith => __$AddInventoryBatchResponseDtoCopyWithImpl<_AddInventoryBatchResponseDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryBatchResponseDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryBatchResponseDto&&(identical(other.requestedCount, requestedCount) || other.requestedCount == requestedCount)&&(identical(other.successCount, successCount) || other.successCount == successCount)&&(identical(other.failedCount, failedCount) || other.failedCount == failedCount)&&const DeepCollectionEquality().equals(other._succeeded, _succeeded)&&const DeepCollectionEquality().equals(other._failed, _failed));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,requestedCount,successCount,failedCount,const DeepCollectionEquality().hash(_succeeded),const DeepCollectionEquality().hash(_failed));

@override
String toString() {
  return 'AddInventoryBatchResponseDto(requestedCount: $requestedCount, successCount: $successCount, failedCount: $failedCount, succeeded: $succeeded, failed: $failed)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryBatchResponseDtoCopyWith<$Res> implements $AddInventoryBatchResponseDtoCopyWith<$Res> {
  factory _$AddInventoryBatchResponseDtoCopyWith(_AddInventoryBatchResponseDto value, $Res Function(_AddInventoryBatchResponseDto) _then) = __$AddInventoryBatchResponseDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'requestedCount') int requestedCount,@JsonKey(name: 'successCount') int successCount,@JsonKey(name: 'failedCount') int failedCount,@JsonKey(name: 'succeeded') List<AddInventoryBatchSucceededRowDto> succeeded,@JsonKey(name: 'failed') List<AddInventoryBatchFailedRowDto> failed
});




}
/// @nodoc
class __$AddInventoryBatchResponseDtoCopyWithImpl<$Res>
    implements _$AddInventoryBatchResponseDtoCopyWith<$Res> {
  __$AddInventoryBatchResponseDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryBatchResponseDto _self;
  final $Res Function(_AddInventoryBatchResponseDto) _then;

/// Create a copy of AddInventoryBatchResponseDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? requestedCount = null,Object? successCount = null,Object? failedCount = null,Object? succeeded = null,Object? failed = null,}) {
  return _then(_AddInventoryBatchResponseDto(
requestedCount: null == requestedCount ? _self.requestedCount : requestedCount // ignore: cast_nullable_to_non_nullable
as int,successCount: null == successCount ? _self.successCount : successCount // ignore: cast_nullable_to_non_nullable
as int,failedCount: null == failedCount ? _self.failedCount : failedCount // ignore: cast_nullable_to_non_nullable
as int,succeeded: null == succeeded ? _self._succeeded : succeeded // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchSucceededRowDto>,failed: null == failed ? _self._failed : failed // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchFailedRowDto>,
  ));
}


}


/// @nodoc
mixin _$AddInventoryBatchSucceededRowDto {

@JsonKey(name: 'clientRowId') String get clientRowId;@JsonKey(name: 'result') AddInventoryResultDto get result;
/// Create a copy of AddInventoryBatchSucceededRowDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryBatchSucceededRowDtoCopyWith<AddInventoryBatchSucceededRowDto> get copyWith => _$AddInventoryBatchSucceededRowDtoCopyWithImpl<AddInventoryBatchSucceededRowDto>(this as AddInventoryBatchSucceededRowDto, _$identity);

  /// Serializes this AddInventoryBatchSucceededRowDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryBatchSucceededRowDto&&(identical(other.clientRowId, clientRowId) || other.clientRowId == clientRowId)&&(identical(other.result, result) || other.result == result));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,clientRowId,result);

@override
String toString() {
  return 'AddInventoryBatchSucceededRowDto(clientRowId: $clientRowId, result: $result)';
}


}

/// @nodoc
abstract mixin class $AddInventoryBatchSucceededRowDtoCopyWith<$Res>  {
  factory $AddInventoryBatchSucceededRowDtoCopyWith(AddInventoryBatchSucceededRowDto value, $Res Function(AddInventoryBatchSucceededRowDto) _then) = _$AddInventoryBatchSucceededRowDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'clientRowId') String clientRowId,@JsonKey(name: 'result') AddInventoryResultDto result
});


$AddInventoryResultDtoCopyWith<$Res> get result;

}
/// @nodoc
class _$AddInventoryBatchSucceededRowDtoCopyWithImpl<$Res>
    implements $AddInventoryBatchSucceededRowDtoCopyWith<$Res> {
  _$AddInventoryBatchSucceededRowDtoCopyWithImpl(this._self, this._then);

  final AddInventoryBatchSucceededRowDto _self;
  final $Res Function(AddInventoryBatchSucceededRowDto) _then;

/// Create a copy of AddInventoryBatchSucceededRowDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? clientRowId = null,Object? result = null,}) {
  return _then(_self.copyWith(
clientRowId: null == clientRowId ? _self.clientRowId : clientRowId // ignore: cast_nullable_to_non_nullable
as String,result: null == result ? _self.result : result // ignore: cast_nullable_to_non_nullable
as AddInventoryResultDto,
  ));
}
/// Create a copy of AddInventoryBatchSucceededRowDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$AddInventoryResultDtoCopyWith<$Res> get result {
  
  return $AddInventoryResultDtoCopyWith<$Res>(_self.result, (value) {
    return _then(_self.copyWith(result: value));
  });
}
}


/// Adds pattern-matching-related methods to [AddInventoryBatchSucceededRowDto].
extension AddInventoryBatchSucceededRowDtoPatterns on AddInventoryBatchSucceededRowDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryBatchSucceededRowDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryBatchSucceededRowDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryBatchSucceededRowDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchSucceededRowDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryBatchSucceededRowDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchSucceededRowDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'result')  AddInventoryResultDto result)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryBatchSucceededRowDto() when $default != null:
return $default(_that.clientRowId,_that.result);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'result')  AddInventoryResultDto result)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchSucceededRowDto():
return $default(_that.clientRowId,_that.result);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'result')  AddInventoryResultDto result)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchSucceededRowDto() when $default != null:
return $default(_that.clientRowId,_that.result);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryBatchSucceededRowDto implements AddInventoryBatchSucceededRowDto {
  const _AddInventoryBatchSucceededRowDto({@JsonKey(name: 'clientRowId') required this.clientRowId, @JsonKey(name: 'result') required this.result});
  factory _AddInventoryBatchSucceededRowDto.fromJson(Map<String, dynamic> json) => _$AddInventoryBatchSucceededRowDtoFromJson(json);

@override@JsonKey(name: 'clientRowId') final  String clientRowId;
@override@JsonKey(name: 'result') final  AddInventoryResultDto result;

/// Create a copy of AddInventoryBatchSucceededRowDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryBatchSucceededRowDtoCopyWith<_AddInventoryBatchSucceededRowDto> get copyWith => __$AddInventoryBatchSucceededRowDtoCopyWithImpl<_AddInventoryBatchSucceededRowDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryBatchSucceededRowDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryBatchSucceededRowDto&&(identical(other.clientRowId, clientRowId) || other.clientRowId == clientRowId)&&(identical(other.result, result) || other.result == result));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,clientRowId,result);

@override
String toString() {
  return 'AddInventoryBatchSucceededRowDto(clientRowId: $clientRowId, result: $result)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryBatchSucceededRowDtoCopyWith<$Res> implements $AddInventoryBatchSucceededRowDtoCopyWith<$Res> {
  factory _$AddInventoryBatchSucceededRowDtoCopyWith(_AddInventoryBatchSucceededRowDto value, $Res Function(_AddInventoryBatchSucceededRowDto) _then) = __$AddInventoryBatchSucceededRowDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'clientRowId') String clientRowId,@JsonKey(name: 'result') AddInventoryResultDto result
});


@override $AddInventoryResultDtoCopyWith<$Res> get result;

}
/// @nodoc
class __$AddInventoryBatchSucceededRowDtoCopyWithImpl<$Res>
    implements _$AddInventoryBatchSucceededRowDtoCopyWith<$Res> {
  __$AddInventoryBatchSucceededRowDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryBatchSucceededRowDto _self;
  final $Res Function(_AddInventoryBatchSucceededRowDto) _then;

/// Create a copy of AddInventoryBatchSucceededRowDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? clientRowId = null,Object? result = null,}) {
  return _then(_AddInventoryBatchSucceededRowDto(
clientRowId: null == clientRowId ? _self.clientRowId : clientRowId // ignore: cast_nullable_to_non_nullable
as String,result: null == result ? _self.result : result // ignore: cast_nullable_to_non_nullable
as AddInventoryResultDto,
  ));
}

/// Create a copy of AddInventoryBatchSucceededRowDto
/// with the given fields replaced by the non-null parameter values.
@override
@pragma('vm:prefer-inline')
$AddInventoryResultDtoCopyWith<$Res> get result {
  
  return $AddInventoryResultDtoCopyWith<$Res>(_self.result, (value) {
    return _then(_self.copyWith(result: value));
  });
}
}


/// @nodoc
mixin _$AddInventoryBatchFailedRowDto {

@JsonKey(name: 'clientRowId') String get clientRowId;@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'barcode') String get barcode;@JsonKey(name: 'errors') List<AddInventoryBatchRowErrorDto> get errors;
/// Create a copy of AddInventoryBatchFailedRowDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryBatchFailedRowDtoCopyWith<AddInventoryBatchFailedRowDto> get copyWith => _$AddInventoryBatchFailedRowDtoCopyWithImpl<AddInventoryBatchFailedRowDto>(this as AddInventoryBatchFailedRowDto, _$identity);

  /// Serializes this AddInventoryBatchFailedRowDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryBatchFailedRowDto&&(identical(other.clientRowId, clientRowId) || other.clientRowId == clientRowId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&const DeepCollectionEquality().equals(other.errors, errors));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,clientRowId,itemName,barcode,const DeepCollectionEquality().hash(errors));

@override
String toString() {
  return 'AddInventoryBatchFailedRowDto(clientRowId: $clientRowId, itemName: $itemName, barcode: $barcode, errors: $errors)';
}


}

/// @nodoc
abstract mixin class $AddInventoryBatchFailedRowDtoCopyWith<$Res>  {
  factory $AddInventoryBatchFailedRowDtoCopyWith(AddInventoryBatchFailedRowDto value, $Res Function(AddInventoryBatchFailedRowDto) _then) = _$AddInventoryBatchFailedRowDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'clientRowId') String clientRowId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'errors') List<AddInventoryBatchRowErrorDto> errors
});




}
/// @nodoc
class _$AddInventoryBatchFailedRowDtoCopyWithImpl<$Res>
    implements $AddInventoryBatchFailedRowDtoCopyWith<$Res> {
  _$AddInventoryBatchFailedRowDtoCopyWithImpl(this._self, this._then);

  final AddInventoryBatchFailedRowDto _self;
  final $Res Function(AddInventoryBatchFailedRowDto) _then;

/// Create a copy of AddInventoryBatchFailedRowDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? clientRowId = null,Object? itemName = null,Object? barcode = null,Object? errors = null,}) {
  return _then(_self.copyWith(
clientRowId: null == clientRowId ? _self.clientRowId : clientRowId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,errors: null == errors ? _self.errors : errors // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchRowErrorDto>,
  ));
}

}


/// Adds pattern-matching-related methods to [AddInventoryBatchFailedRowDto].
extension AddInventoryBatchFailedRowDtoPatterns on AddInventoryBatchFailedRowDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryBatchFailedRowDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryBatchFailedRowDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryBatchFailedRowDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchFailedRowDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryBatchFailedRowDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchFailedRowDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'errors')  List<AddInventoryBatchRowErrorDto> errors)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryBatchFailedRowDto() when $default != null:
return $default(_that.clientRowId,_that.itemName,_that.barcode,_that.errors);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'errors')  List<AddInventoryBatchRowErrorDto> errors)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchFailedRowDto():
return $default(_that.clientRowId,_that.itemName,_that.barcode,_that.errors);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'clientRowId')  String clientRowId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'errors')  List<AddInventoryBatchRowErrorDto> errors)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchFailedRowDto() when $default != null:
return $default(_that.clientRowId,_that.itemName,_that.barcode,_that.errors);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryBatchFailedRowDto implements AddInventoryBatchFailedRowDto {
  const _AddInventoryBatchFailedRowDto({@JsonKey(name: 'clientRowId') required this.clientRowId, @JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'barcode') required this.barcode, @JsonKey(name: 'errors') final  List<AddInventoryBatchRowErrorDto> errors = const []}): _errors = errors;
  factory _AddInventoryBatchFailedRowDto.fromJson(Map<String, dynamic> json) => _$AddInventoryBatchFailedRowDtoFromJson(json);

@override@JsonKey(name: 'clientRowId') final  String clientRowId;
@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'barcode') final  String barcode;
 final  List<AddInventoryBatchRowErrorDto> _errors;
@override@JsonKey(name: 'errors') List<AddInventoryBatchRowErrorDto> get errors {
  if (_errors is EqualUnmodifiableListView) return _errors;
  // ignore: implicit_dynamic_type
  return EqualUnmodifiableListView(_errors);
}


/// Create a copy of AddInventoryBatchFailedRowDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryBatchFailedRowDtoCopyWith<_AddInventoryBatchFailedRowDto> get copyWith => __$AddInventoryBatchFailedRowDtoCopyWithImpl<_AddInventoryBatchFailedRowDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryBatchFailedRowDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryBatchFailedRowDto&&(identical(other.clientRowId, clientRowId) || other.clientRowId == clientRowId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&const DeepCollectionEquality().equals(other._errors, _errors));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,clientRowId,itemName,barcode,const DeepCollectionEquality().hash(_errors));

@override
String toString() {
  return 'AddInventoryBatchFailedRowDto(clientRowId: $clientRowId, itemName: $itemName, barcode: $barcode, errors: $errors)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryBatchFailedRowDtoCopyWith<$Res> implements $AddInventoryBatchFailedRowDtoCopyWith<$Res> {
  factory _$AddInventoryBatchFailedRowDtoCopyWith(_AddInventoryBatchFailedRowDto value, $Res Function(_AddInventoryBatchFailedRowDto) _then) = __$AddInventoryBatchFailedRowDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'clientRowId') String clientRowId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'errors') List<AddInventoryBatchRowErrorDto> errors
});




}
/// @nodoc
class __$AddInventoryBatchFailedRowDtoCopyWithImpl<$Res>
    implements _$AddInventoryBatchFailedRowDtoCopyWith<$Res> {
  __$AddInventoryBatchFailedRowDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryBatchFailedRowDto _self;
  final $Res Function(_AddInventoryBatchFailedRowDto) _then;

/// Create a copy of AddInventoryBatchFailedRowDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? clientRowId = null,Object? itemName = null,Object? barcode = null,Object? errors = null,}) {
  return _then(_AddInventoryBatchFailedRowDto(
clientRowId: null == clientRowId ? _self.clientRowId : clientRowId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,errors: null == errors ? _self._errors : errors // ignore: cast_nullable_to_non_nullable
as List<AddInventoryBatchRowErrorDto>,
  ));
}


}


/// @nodoc
mixin _$AddInventoryResultDto {

@JsonKey(name: 'itemId') String get itemId;@JsonKey(name: 'itemName') String get itemName;@JsonKey(name: 'barcode') String get barcode;@JsonKey(name: 'inventoryBatchId') String get inventoryBatchId;@JsonKey(name: 'batchNumber') String get batchNumber;@JsonKey(name: 'batchQuantity') double get batchQuantity;@JsonKey(name: 'totalQuantity') double get totalQuantity;@JsonKey(name: 'supplierId') String? get supplierId;@JsonKey(name: 'stockTransactionId') String get stockTransactionId;@JsonKey(name: 'performedAt') DateTime get performedAt;
/// Create a copy of AddInventoryResultDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryResultDtoCopyWith<AddInventoryResultDto> get copyWith => _$AddInventoryResultDtoCopyWithImpl<AddInventoryResultDto>(this as AddInventoryResultDto, _$identity);

  /// Serializes this AddInventoryResultDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryResultDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.batchQuantity, batchQuantity) || other.batchQuantity == batchQuantity)&&(identical(other.totalQuantity, totalQuantity) || other.totalQuantity == totalQuantity)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.stockTransactionId, stockTransactionId) || other.stockTransactionId == stockTransactionId)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,itemName,barcode,inventoryBatchId,batchNumber,batchQuantity,totalQuantity,supplierId,stockTransactionId,performedAt);

@override
String toString() {
  return 'AddInventoryResultDto(itemId: $itemId, itemName: $itemName, barcode: $barcode, inventoryBatchId: $inventoryBatchId, batchNumber: $batchNumber, batchQuantity: $batchQuantity, totalQuantity: $totalQuantity, supplierId: $supplierId, stockTransactionId: $stockTransactionId, performedAt: $performedAt)';
}


}

/// @nodoc
abstract mixin class $AddInventoryResultDtoCopyWith<$Res>  {
  factory $AddInventoryResultDtoCopyWith(AddInventoryResultDto value, $Res Function(AddInventoryResultDto) _then) = _$AddInventoryResultDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'itemId') String itemId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'inventoryBatchId') String inventoryBatchId,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'batchQuantity') double batchQuantity,@JsonKey(name: 'totalQuantity') double totalQuantity,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'stockTransactionId') String stockTransactionId,@JsonKey(name: 'performedAt') DateTime performedAt
});




}
/// @nodoc
class _$AddInventoryResultDtoCopyWithImpl<$Res>
    implements $AddInventoryResultDtoCopyWith<$Res> {
  _$AddInventoryResultDtoCopyWithImpl(this._self, this._then);

  final AddInventoryResultDto _self;
  final $Res Function(AddInventoryResultDto) _then;

/// Create a copy of AddInventoryResultDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? itemId = null,Object? itemName = null,Object? barcode = null,Object? inventoryBatchId = null,Object? batchNumber = null,Object? batchQuantity = null,Object? totalQuantity = null,Object? supplierId = freezed,Object? stockTransactionId = null,Object? performedAt = null,}) {
  return _then(_self.copyWith(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,inventoryBatchId: null == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,batchQuantity: null == batchQuantity ? _self.batchQuantity : batchQuantity // ignore: cast_nullable_to_non_nullable
as double,totalQuantity: null == totalQuantity ? _self.totalQuantity : totalQuantity // ignore: cast_nullable_to_non_nullable
as double,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,stockTransactionId: null == stockTransactionId ? _self.stockTransactionId : stockTransactionId // ignore: cast_nullable_to_non_nullable
as String,performedAt: null == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}

}


/// Adds pattern-matching-related methods to [AddInventoryResultDto].
extension AddInventoryResultDtoPatterns on AddInventoryResultDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryResultDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryResultDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryResultDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryResultDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryResultDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryResultDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'inventoryBatchId')  String inventoryBatchId, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'batchQuantity')  double batchQuantity, @JsonKey(name: 'totalQuantity')  double totalQuantity, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'stockTransactionId')  String stockTransactionId, @JsonKey(name: 'performedAt')  DateTime performedAt)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryResultDto() when $default != null:
return $default(_that.itemId,_that.itemName,_that.barcode,_that.inventoryBatchId,_that.batchNumber,_that.batchQuantity,_that.totalQuantity,_that.supplierId,_that.stockTransactionId,_that.performedAt);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'inventoryBatchId')  String inventoryBatchId, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'batchQuantity')  double batchQuantity, @JsonKey(name: 'totalQuantity')  double totalQuantity, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'stockTransactionId')  String stockTransactionId, @JsonKey(name: 'performedAt')  DateTime performedAt)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryResultDto():
return $default(_that.itemId,_that.itemName,_that.barcode,_that.inventoryBatchId,_that.batchNumber,_that.batchQuantity,_that.totalQuantity,_that.supplierId,_that.stockTransactionId,_that.performedAt);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'itemId')  String itemId, @JsonKey(name: 'itemName')  String itemName, @JsonKey(name: 'barcode')  String barcode, @JsonKey(name: 'inventoryBatchId')  String inventoryBatchId, @JsonKey(name: 'batchNumber')  String batchNumber, @JsonKey(name: 'batchQuantity')  double batchQuantity, @JsonKey(name: 'totalQuantity')  double totalQuantity, @JsonKey(name: 'supplierId')  String? supplierId, @JsonKey(name: 'stockTransactionId')  String stockTransactionId, @JsonKey(name: 'performedAt')  DateTime performedAt)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryResultDto() when $default != null:
return $default(_that.itemId,_that.itemName,_that.barcode,_that.inventoryBatchId,_that.batchNumber,_that.batchQuantity,_that.totalQuantity,_that.supplierId,_that.stockTransactionId,_that.performedAt);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryResultDto implements AddInventoryResultDto {
  const _AddInventoryResultDto({@JsonKey(name: 'itemId') required this.itemId, @JsonKey(name: 'itemName') required this.itemName, @JsonKey(name: 'barcode') required this.barcode, @JsonKey(name: 'inventoryBatchId') required this.inventoryBatchId, @JsonKey(name: 'batchNumber') required this.batchNumber, @JsonKey(name: 'batchQuantity') required this.batchQuantity, @JsonKey(name: 'totalQuantity') required this.totalQuantity, @JsonKey(name: 'supplierId') this.supplierId, @JsonKey(name: 'stockTransactionId') required this.stockTransactionId, @JsonKey(name: 'performedAt') required this.performedAt});
  factory _AddInventoryResultDto.fromJson(Map<String, dynamic> json) => _$AddInventoryResultDtoFromJson(json);

@override@JsonKey(name: 'itemId') final  String itemId;
@override@JsonKey(name: 'itemName') final  String itemName;
@override@JsonKey(name: 'barcode') final  String barcode;
@override@JsonKey(name: 'inventoryBatchId') final  String inventoryBatchId;
@override@JsonKey(name: 'batchNumber') final  String batchNumber;
@override@JsonKey(name: 'batchQuantity') final  double batchQuantity;
@override@JsonKey(name: 'totalQuantity') final  double totalQuantity;
@override@JsonKey(name: 'supplierId') final  String? supplierId;
@override@JsonKey(name: 'stockTransactionId') final  String stockTransactionId;
@override@JsonKey(name: 'performedAt') final  DateTime performedAt;

/// Create a copy of AddInventoryResultDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryResultDtoCopyWith<_AddInventoryResultDto> get copyWith => __$AddInventoryResultDtoCopyWithImpl<_AddInventoryResultDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryResultDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryResultDto&&(identical(other.itemId, itemId) || other.itemId == itemId)&&(identical(other.itemName, itemName) || other.itemName == itemName)&&(identical(other.barcode, barcode) || other.barcode == barcode)&&(identical(other.inventoryBatchId, inventoryBatchId) || other.inventoryBatchId == inventoryBatchId)&&(identical(other.batchNumber, batchNumber) || other.batchNumber == batchNumber)&&(identical(other.batchQuantity, batchQuantity) || other.batchQuantity == batchQuantity)&&(identical(other.totalQuantity, totalQuantity) || other.totalQuantity == totalQuantity)&&(identical(other.supplierId, supplierId) || other.supplierId == supplierId)&&(identical(other.stockTransactionId, stockTransactionId) || other.stockTransactionId == stockTransactionId)&&(identical(other.performedAt, performedAt) || other.performedAt == performedAt));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,itemId,itemName,barcode,inventoryBatchId,batchNumber,batchQuantity,totalQuantity,supplierId,stockTransactionId,performedAt);

@override
String toString() {
  return 'AddInventoryResultDto(itemId: $itemId, itemName: $itemName, barcode: $barcode, inventoryBatchId: $inventoryBatchId, batchNumber: $batchNumber, batchQuantity: $batchQuantity, totalQuantity: $totalQuantity, supplierId: $supplierId, stockTransactionId: $stockTransactionId, performedAt: $performedAt)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryResultDtoCopyWith<$Res> implements $AddInventoryResultDtoCopyWith<$Res> {
  factory _$AddInventoryResultDtoCopyWith(_AddInventoryResultDto value, $Res Function(_AddInventoryResultDto) _then) = __$AddInventoryResultDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'itemId') String itemId,@JsonKey(name: 'itemName') String itemName,@JsonKey(name: 'barcode') String barcode,@JsonKey(name: 'inventoryBatchId') String inventoryBatchId,@JsonKey(name: 'batchNumber') String batchNumber,@JsonKey(name: 'batchQuantity') double batchQuantity,@JsonKey(name: 'totalQuantity') double totalQuantity,@JsonKey(name: 'supplierId') String? supplierId,@JsonKey(name: 'stockTransactionId') String stockTransactionId,@JsonKey(name: 'performedAt') DateTime performedAt
});




}
/// @nodoc
class __$AddInventoryResultDtoCopyWithImpl<$Res>
    implements _$AddInventoryResultDtoCopyWith<$Res> {
  __$AddInventoryResultDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryResultDto _self;
  final $Res Function(_AddInventoryResultDto) _then;

/// Create a copy of AddInventoryResultDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? itemId = null,Object? itemName = null,Object? barcode = null,Object? inventoryBatchId = null,Object? batchNumber = null,Object? batchQuantity = null,Object? totalQuantity = null,Object? supplierId = freezed,Object? stockTransactionId = null,Object? performedAt = null,}) {
  return _then(_AddInventoryResultDto(
itemId: null == itemId ? _self.itemId : itemId // ignore: cast_nullable_to_non_nullable
as String,itemName: null == itemName ? _self.itemName : itemName // ignore: cast_nullable_to_non_nullable
as String,barcode: null == barcode ? _self.barcode : barcode // ignore: cast_nullable_to_non_nullable
as String,inventoryBatchId: null == inventoryBatchId ? _self.inventoryBatchId : inventoryBatchId // ignore: cast_nullable_to_non_nullable
as String,batchNumber: null == batchNumber ? _self.batchNumber : batchNumber // ignore: cast_nullable_to_non_nullable
as String,batchQuantity: null == batchQuantity ? _self.batchQuantity : batchQuantity // ignore: cast_nullable_to_non_nullable
as double,totalQuantity: null == totalQuantity ? _self.totalQuantity : totalQuantity // ignore: cast_nullable_to_non_nullable
as double,supplierId: freezed == supplierId ? _self.supplierId : supplierId // ignore: cast_nullable_to_non_nullable
as String?,stockTransactionId: null == stockTransactionId ? _self.stockTransactionId : stockTransactionId // ignore: cast_nullable_to_non_nullable
as String,performedAt: null == performedAt ? _self.performedAt : performedAt // ignore: cast_nullable_to_non_nullable
as DateTime,
  ));
}


}


/// @nodoc
mixin _$AddInventoryBatchRowErrorDto {

@JsonKey(name: 'code') String get code;@JsonKey(name: 'description') String get description;
/// Create a copy of AddInventoryBatchRowErrorDto
/// with the given fields replaced by the non-null parameter values.
@JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
$AddInventoryBatchRowErrorDtoCopyWith<AddInventoryBatchRowErrorDto> get copyWith => _$AddInventoryBatchRowErrorDtoCopyWithImpl<AddInventoryBatchRowErrorDto>(this as AddInventoryBatchRowErrorDto, _$identity);

  /// Serializes this AddInventoryBatchRowErrorDto to a JSON map.
  Map<String, dynamic> toJson();


@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is AddInventoryBatchRowErrorDto&&(identical(other.code, code) || other.code == code)&&(identical(other.description, description) || other.description == description));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,code,description);

@override
String toString() {
  return 'AddInventoryBatchRowErrorDto(code: $code, description: $description)';
}


}

/// @nodoc
abstract mixin class $AddInventoryBatchRowErrorDtoCopyWith<$Res>  {
  factory $AddInventoryBatchRowErrorDtoCopyWith(AddInventoryBatchRowErrorDto value, $Res Function(AddInventoryBatchRowErrorDto) _then) = _$AddInventoryBatchRowErrorDtoCopyWithImpl;
@useResult
$Res call({
@JsonKey(name: 'code') String code,@JsonKey(name: 'description') String description
});




}
/// @nodoc
class _$AddInventoryBatchRowErrorDtoCopyWithImpl<$Res>
    implements $AddInventoryBatchRowErrorDtoCopyWith<$Res> {
  _$AddInventoryBatchRowErrorDtoCopyWithImpl(this._self, this._then);

  final AddInventoryBatchRowErrorDto _self;
  final $Res Function(AddInventoryBatchRowErrorDto) _then;

/// Create a copy of AddInventoryBatchRowErrorDto
/// with the given fields replaced by the non-null parameter values.
@pragma('vm:prefer-inline') @override $Res call({Object? code = null,Object? description = null,}) {
  return _then(_self.copyWith(
code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,
  ));
}

}


/// Adds pattern-matching-related methods to [AddInventoryBatchRowErrorDto].
extension AddInventoryBatchRowErrorDtoPatterns on AddInventoryBatchRowErrorDto {
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

@optionalTypeArgs TResult maybeMap<TResult extends Object?>(TResult Function( _AddInventoryBatchRowErrorDto value)?  $default,{required TResult orElse(),}){
final _that = this;
switch (_that) {
case _AddInventoryBatchRowErrorDto() when $default != null:
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

@optionalTypeArgs TResult map<TResult extends Object?>(TResult Function( _AddInventoryBatchRowErrorDto value)  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchRowErrorDto():
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

@optionalTypeArgs TResult? mapOrNull<TResult extends Object?>(TResult? Function( _AddInventoryBatchRowErrorDto value)?  $default,){
final _that = this;
switch (_that) {
case _AddInventoryBatchRowErrorDto() when $default != null:
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

@optionalTypeArgs TResult maybeWhen<TResult extends Object?>(TResult Function(@JsonKey(name: 'code')  String code, @JsonKey(name: 'description')  String description)?  $default,{required TResult orElse(),}) {final _that = this;
switch (_that) {
case _AddInventoryBatchRowErrorDto() when $default != null:
return $default(_that.code,_that.description);case _:
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

@optionalTypeArgs TResult when<TResult extends Object?>(TResult Function(@JsonKey(name: 'code')  String code, @JsonKey(name: 'description')  String description)  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchRowErrorDto():
return $default(_that.code,_that.description);}
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

@optionalTypeArgs TResult? whenOrNull<TResult extends Object?>(TResult? Function(@JsonKey(name: 'code')  String code, @JsonKey(name: 'description')  String description)?  $default,) {final _that = this;
switch (_that) {
case _AddInventoryBatchRowErrorDto() when $default != null:
return $default(_that.code,_that.description);case _:
  return null;

}
}

}

/// @nodoc
@JsonSerializable()

class _AddInventoryBatchRowErrorDto implements AddInventoryBatchRowErrorDto {
  const _AddInventoryBatchRowErrorDto({@JsonKey(name: 'code') required this.code, @JsonKey(name: 'description') required this.description});
  factory _AddInventoryBatchRowErrorDto.fromJson(Map<String, dynamic> json) => _$AddInventoryBatchRowErrorDtoFromJson(json);

@override@JsonKey(name: 'code') final  String code;
@override@JsonKey(name: 'description') final  String description;

/// Create a copy of AddInventoryBatchRowErrorDto
/// with the given fields replaced by the non-null parameter values.
@override @JsonKey(includeFromJson: false, includeToJson: false)
@pragma('vm:prefer-inline')
_$AddInventoryBatchRowErrorDtoCopyWith<_AddInventoryBatchRowErrorDto> get copyWith => __$AddInventoryBatchRowErrorDtoCopyWithImpl<_AddInventoryBatchRowErrorDto>(this, _$identity);

@override
Map<String, dynamic> toJson() {
  return _$AddInventoryBatchRowErrorDtoToJson(this, );
}

@override
bool operator ==(Object other) {
  return identical(this, other) || (other.runtimeType == runtimeType&&other is _AddInventoryBatchRowErrorDto&&(identical(other.code, code) || other.code == code)&&(identical(other.description, description) || other.description == description));
}

@JsonKey(includeFromJson: false, includeToJson: false)
@override
int get hashCode => Object.hash(runtimeType,code,description);

@override
String toString() {
  return 'AddInventoryBatchRowErrorDto(code: $code, description: $description)';
}


}

/// @nodoc
abstract mixin class _$AddInventoryBatchRowErrorDtoCopyWith<$Res> implements $AddInventoryBatchRowErrorDtoCopyWith<$Res> {
  factory _$AddInventoryBatchRowErrorDtoCopyWith(_AddInventoryBatchRowErrorDto value, $Res Function(_AddInventoryBatchRowErrorDto) _then) = __$AddInventoryBatchRowErrorDtoCopyWithImpl;
@override @useResult
$Res call({
@JsonKey(name: 'code') String code,@JsonKey(name: 'description') String description
});




}
/// @nodoc
class __$AddInventoryBatchRowErrorDtoCopyWithImpl<$Res>
    implements _$AddInventoryBatchRowErrorDtoCopyWith<$Res> {
  __$AddInventoryBatchRowErrorDtoCopyWithImpl(this._self, this._then);

  final _AddInventoryBatchRowErrorDto _self;
  final $Res Function(_AddInventoryBatchRowErrorDto) _then;

/// Create a copy of AddInventoryBatchRowErrorDto
/// with the given fields replaced by the non-null parameter values.
@override @pragma('vm:prefer-inline') $Res call({Object? code = null,Object? description = null,}) {
  return _then(_AddInventoryBatchRowErrorDto(
code: null == code ? _self.code : code // ignore: cast_nullable_to_non_nullable
as String,description: null == description ? _self.description : description // ignore: cast_nullable_to_non_nullable
as String,
  ));
}


}

// dart format on
