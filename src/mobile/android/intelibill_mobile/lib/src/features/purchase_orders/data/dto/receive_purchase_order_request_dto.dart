import 'package:freezed_annotation/freezed_annotation.dart';

part 'receive_purchase_order_request_dto.freezed.dart';
part 'receive_purchase_order_request_dto.g.dart';

String _trim(String value) => value.trim();

String? _trimNullable(String? value) {
  final trimmed = value?.trim();
  return trimmed == null || trimmed.isEmpty ? null : trimmed;
}

String? _dateOnlyToJson(DateTime? value) {
  if (value == null) return null;

  final year = value.year.toString().padLeft(4, '0');
  final month = value.month.toString().padLeft(2, '0');
  final day = value.day.toString().padLeft(2, '0');
  return '$year-$month-$day';
}

int _quantityToJson(double value) => value.round();

List<Map<String, dynamic>> _receiveLineRequestDtosToJson(
  List<ReceivePurchaseOrderLineRequestDto> value,
) {
  return value.map((line) => line.toJson()).toList(growable: false);
}

@freezed
sealed class ReceivePurchaseOrderLineRequestDto
    with _$ReceivePurchaseOrderLineRequestDto {
  const factory ReceivePurchaseOrderLineRequestDto({
    required String purchaseOrderLineId,
    @JsonKey(toJson: _trim) required String barcode,
    @JsonKey(toJson: _trim) required String batchNumber,
    @JsonKey(toJson: _quantityToJson) required double quantity,
    required double totalPurchaseCost,
    @JsonKey(includeToJson: false) required double unitCost,
    required double mrp,
    required double salesPrice,
    required double taxRatePercent,
    required bool taxIncluded,
    required bool purchaseTaxIncluded,
    @JsonKey(toJson: _dateOnlyToJson) DateTime? expiryDate,
    @JsonKey(toJson: _dateOnlyToJson) DateTime? manufacturingDate,
  }) = _ReceivePurchaseOrderLineRequestDto;

  factory ReceivePurchaseOrderLineRequestDto.fromJson(
    Map<String, dynamic> json,
  ) => _$ReceivePurchaseOrderLineRequestDtoFromJson(json);
}

@freezed
sealed class ReceivePurchaseOrderRequestDto
    with _$ReceivePurchaseOrderRequestDto {
  const factory ReceivePurchaseOrderRequestDto({
    @JsonKey(toJson: _trimNullable) String? referenceNumber,
    @JsonKey(toJson: _trimNullable) String? notes,
    required DateTime receivedAt,
    @JsonKey(toJson: _receiveLineRequestDtosToJson)
    required List<ReceivePurchaseOrderLineRequestDto> lines,
  }) = _ReceivePurchaseOrderRequestDto;

  factory ReceivePurchaseOrderRequestDto.fromJson(Map<String, dynamic> json) =>
      _$ReceivePurchaseOrderRequestDtoFromJson(json);
}
