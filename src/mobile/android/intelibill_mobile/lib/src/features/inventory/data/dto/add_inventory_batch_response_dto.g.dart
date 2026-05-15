// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_inventory_batch_response_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AddInventoryBatchResponseDto _$AddInventoryBatchResponseDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryBatchResponseDto(
  requestedCount: (json['requestedCount'] as num).toInt(),
  successCount: (json['successCount'] as num).toInt(),
  failedCount: (json['failedCount'] as num).toInt(),
  succeeded:
      (json['succeeded'] as List<dynamic>?)
          ?.map(
            (e) => AddInventoryBatchSucceededRowDto.fromJson(
              e as Map<String, dynamic>,
            ),
          )
          .toList() ??
      const [],
  failed:
      (json['failed'] as List<dynamic>?)
          ?.map(
            (e) => AddInventoryBatchFailedRowDto.fromJson(
              e as Map<String, dynamic>,
            ),
          )
          .toList() ??
      const [],
);

Map<String, dynamic> _$AddInventoryBatchResponseDtoToJson(
  _AddInventoryBatchResponseDto instance,
) => <String, dynamic>{
  'requestedCount': instance.requestedCount,
  'successCount': instance.successCount,
  'failedCount': instance.failedCount,
  'succeeded': instance.succeeded,
  'failed': instance.failed,
};

_AddInventoryBatchSucceededRowDto _$AddInventoryBatchSucceededRowDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryBatchSucceededRowDto(
  clientRowId: json['clientRowId'] as String,
  result: AddInventoryResultDto.fromJson(
    json['result'] as Map<String, dynamic>,
  ),
);

Map<String, dynamic> _$AddInventoryBatchSucceededRowDtoToJson(
  _AddInventoryBatchSucceededRowDto instance,
) => <String, dynamic>{
  'clientRowId': instance.clientRowId,
  'result': instance.result,
};

_AddInventoryBatchFailedRowDto _$AddInventoryBatchFailedRowDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryBatchFailedRowDto(
  clientRowId: json['clientRowId'] as String,
  itemName: json['itemName'] as String,
  barcode: json['barcode'] as String,
  errors:
      (json['errors'] as List<dynamic>?)
          ?.map(
            (e) => AddInventoryBatchRowErrorDto.fromJson(
              e as Map<String, dynamic>,
            ),
          )
          .toList() ??
      const [],
);

Map<String, dynamic> _$AddInventoryBatchFailedRowDtoToJson(
  _AddInventoryBatchFailedRowDto instance,
) => <String, dynamic>{
  'clientRowId': instance.clientRowId,
  'itemName': instance.itemName,
  'barcode': instance.barcode,
  'errors': instance.errors,
};

_AddInventoryResultDto _$AddInventoryResultDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryResultDto(
  itemId: json['itemId'] as String,
  itemName: json['itemName'] as String,
  barcode: json['barcode'] as String,
  inventoryBatchId: json['inventoryBatchId'] as String,
  batchNumber: json['batchNumber'] as String,
  batchQuantity: (json['batchQuantity'] as num).toDouble(),
  totalQuantity: (json['totalQuantity'] as num).toDouble(),
  supplierId: json['supplierId'] as String?,
  stockTransactionId: json['stockTransactionId'] as String,
  performedAt: DateTime.parse(json['performedAt'] as String),
);

Map<String, dynamic> _$AddInventoryResultDtoToJson(
  _AddInventoryResultDto instance,
) => <String, dynamic>{
  'itemId': instance.itemId,
  'itemName': instance.itemName,
  'barcode': instance.barcode,
  'inventoryBatchId': instance.inventoryBatchId,
  'batchNumber': instance.batchNumber,
  'batchQuantity': instance.batchQuantity,
  'totalQuantity': instance.totalQuantity,
  'supplierId': instance.supplierId,
  'stockTransactionId': instance.stockTransactionId,
  'performedAt': instance.performedAt.toIso8601String(),
};

_AddInventoryBatchRowErrorDto _$AddInventoryBatchRowErrorDtoFromJson(
  Map<String, dynamic> json,
) => _AddInventoryBatchRowErrorDto(
  code: json['code'] as String,
  description: json['description'] as String,
);

Map<String, dynamic> _$AddInventoryBatchRowErrorDtoToJson(
  _AddInventoryBatchRowErrorDto instance,
) => <String, dynamic>{
  'code': instance.code,
  'description': instance.description,
};
