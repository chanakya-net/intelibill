// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'credit_note_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_CreditNoteListItemDto _$CreditNoteListItemDtoFromJson(
  Map<String, dynamic> json,
) => _CreditNoteListItemDto(
  creditNoteId: json['creditNoteId'] as String,
  code: json['code'] as String,
  status: json['status'] as String,
  originalAmount: (json['originalAmount'] as num).toDouble(),
  availableBalance: (json['availableBalance'] as num).toDouble(),
  expiresAt: json['expiresAt'] == null
      ? null
      : DateTime.parse(json['expiresAt'] as String),
  issuedAt: DateTime.parse(json['issuedAt'] as String),
  saleReturnId: json['saleReturnId'] as String,
  returnNumber: json['returnNumber'] as String,
  saleId: json['saleId'] as String,
  invoiceNumber: json['invoiceNumber'] as String,
  customerName: json['customerName'] as String?,
);

Map<String, dynamic> _$CreditNoteListItemDtoToJson(
  _CreditNoteListItemDto instance,
) => <String, dynamic>{
  'creditNoteId': instance.creditNoteId,
  'code': instance.code,
  'status': instance.status,
  'originalAmount': instance.originalAmount,
  'availableBalance': instance.availableBalance,
  'expiresAt': instance.expiresAt?.toIso8601String(),
  'issuedAt': instance.issuedAt.toIso8601String(),
  'saleReturnId': instance.saleReturnId,
  'returnNumber': instance.returnNumber,
  'saleId': instance.saleId,
  'invoiceNumber': instance.invoiceNumber,
  'customerName': instance.customerName,
};

_CreditNoteDto _$CreditNoteDtoFromJson(Map<String, dynamic> json) =>
    _CreditNoteDto(
      creditNoteId: json['creditNoteId'] as String,
      code: json['code'] as String,
      status: json['status'] as String,
      originalAmount: (json['originalAmount'] as num).toDouble(),
      availableBalance: (json['availableBalance'] as num).toDouble(),
      expiresAt: json['expiresAt'] == null
          ? null
          : DateTime.parse(json['expiresAt'] as String),
      isVoided: json['isVoided'] as bool,
      saleReturnId: json['saleReturnId'] as String,
      reason: json['reason'] as String,
      voidReason: json['voidReason'] as String?,
      returnNumber: json['returnNumber'] as String,
      invoiceNumber: json['invoiceNumber'] as String,
      customerName: json['customerName'] as String?,
    );

Map<String, dynamic> _$CreditNoteDtoToJson(_CreditNoteDto instance) =>
    <String, dynamic>{
      'creditNoteId': instance.creditNoteId,
      'code': instance.code,
      'status': instance.status,
      'originalAmount': instance.originalAmount,
      'availableBalance': instance.availableBalance,
      'expiresAt': instance.expiresAt?.toIso8601String(),
      'isVoided': instance.isVoided,
      'saleReturnId': instance.saleReturnId,
      'reason': instance.reason,
      'voidReason': instance.voidReason,
      'returnNumber': instance.returnNumber,
      'invoiceNumber': instance.invoiceNumber,
      'customerName': instance.customerName,
    };

_CreditNotePrintDto _$CreditNotePrintDtoFromJson(Map<String, dynamic> json) =>
    _CreditNotePrintDto(
      creditNoteId: json['creditNoteId'] as String,
      code: json['code'] as String,
      status: json['status'] as String,
      isUsable: json['isUsable'] as bool,
      originalAmount: (json['originalAmount'] as num).toDouble(),
      availableBalance: (json['availableBalance'] as num).toDouble(),
      issuedAt: DateTime.parse(json['issuedAt'] as String),
      expiresAt: json['expiresAt'] == null
          ? null
          : DateTime.parse(json['expiresAt'] as String),
      saleId: json['saleId'] as String,
      invoiceNumber: json['invoiceNumber'] as String,
      saleReturnId: json['saleReturnId'] as String,
      returnNumber: json['returnNumber'] as String,
      customerDisplayName: json['customerDisplayName'] as String,
      reason: json['reason'] as String,
      voidReason: json['voidReason'] as String?,
    );

Map<String, dynamic> _$CreditNotePrintDtoToJson(_CreditNotePrintDto instance) =>
    <String, dynamic>{
      'creditNoteId': instance.creditNoteId,
      'code': instance.code,
      'status': instance.status,
      'isUsable': instance.isUsable,
      'originalAmount': instance.originalAmount,
      'availableBalance': instance.availableBalance,
      'issuedAt': instance.issuedAt.toIso8601String(),
      'expiresAt': instance.expiresAt?.toIso8601String(),
      'saleId': instance.saleId,
      'invoiceNumber': instance.invoiceNumber,
      'saleReturnId': instance.saleReturnId,
      'returnNumber': instance.returnNumber,
      'customerDisplayName': instance.customerDisplayName,
      'reason': instance.reason,
      'voidReason': instance.voidReason,
    };

_CreditNotesResponseDto _$CreditNotesResponseDtoFromJson(
  Map<String, dynamic> json,
) => _CreditNotesResponseDto(
  items:
      (json['items'] as List<dynamic>?)
          ?.map(
            (e) => CreditNoteListItemDto.fromJson(e as Map<String, dynamic>),
          )
          .toList() ??
      const [],
  totalCount: (json['totalCount'] as num).toInt(),
  pageNumber: (json['pageNumber'] as num).toInt(),
  pageSize: (json['pageSize'] as num).toInt(),
);

Map<String, dynamic> _$CreditNotesResponseDtoToJson(
  _CreditNotesResponseDto instance,
) => <String, dynamic>{
  'items': instance.items,
  'totalCount': instance.totalCount,
  'pageNumber': instance.pageNumber,
  'pageSize': instance.pageSize,
};
