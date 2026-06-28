import 'package:freezed_annotation/freezed_annotation.dart';

part 'credit_note_dto.freezed.dart';
part 'credit_note_dto.g.dart';

@freezed
sealed class CreditNoteListItemDto with _$CreditNoteListItemDto {
  const factory CreditNoteListItemDto({
    @JsonKey(name: 'creditNoteId') required String creditNoteId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'status') required String status,
    @JsonKey(name: 'originalAmount') required double originalAmount,
    @JsonKey(name: 'availableBalance') required double availableBalance,
    @JsonKey(name: 'expiresAt') DateTime? expiresAt,
    @JsonKey(name: 'issuedAt') required DateTime issuedAt,
    @JsonKey(name: 'saleReturnId') required String saleReturnId,
    @JsonKey(name: 'returnNumber') required String returnNumber,
    @JsonKey(name: 'saleId') required String saleId,
    @JsonKey(name: 'invoiceNumber') required String invoiceNumber,
    @JsonKey(name: 'customerName') String? customerName,
  }) = _CreditNoteListItemDto;

  factory CreditNoteListItemDto.fromJson(Map<String, dynamic> json) =>
      _$CreditNoteListItemDtoFromJson(json);
}

@freezed
sealed class CreditNoteDto with _$CreditNoteDto {
  const factory CreditNoteDto({
    @JsonKey(name: 'creditNoteId') required String creditNoteId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'status') required String status,
    @JsonKey(name: 'originalAmount') required double originalAmount,
    @JsonKey(name: 'availableBalance') required double availableBalance,
    @JsonKey(name: 'expiresAt') DateTime? expiresAt,
    @JsonKey(name: 'isVoided') required bool isVoided,
    @JsonKey(name: 'saleReturnId') required String saleReturnId,
    @JsonKey(name: 'reason') required String reason,
    @JsonKey(name: 'voidReason') String? voidReason,
    @JsonKey(name: 'returnNumber') required String returnNumber,
    @JsonKey(name: 'invoiceNumber') required String invoiceNumber,
    @JsonKey(name: 'customerName') String? customerName,
  }) = _CreditNoteDto;

  factory CreditNoteDto.fromJson(Map<String, dynamic> json) =>
      _$CreditNoteDtoFromJson(json);
}

@freezed
sealed class CreditNotePrintDto with _$CreditNotePrintDto {
  const factory CreditNotePrintDto({
    @JsonKey(name: 'creditNoteId') required String creditNoteId,
    @JsonKey(name: 'code') required String code,
    @JsonKey(name: 'status') required String status,
    @JsonKey(name: 'isUsable') required bool isUsable,
    @JsonKey(name: 'originalAmount') required double originalAmount,
    @JsonKey(name: 'availableBalance') required double availableBalance,
    @JsonKey(name: 'issuedAt') required DateTime issuedAt,
    @JsonKey(name: 'expiresAt') DateTime? expiresAt,
    @JsonKey(name: 'saleId') required String saleId,
    @JsonKey(name: 'invoiceNumber') required String invoiceNumber,
    @JsonKey(name: 'saleReturnId') required String saleReturnId,
    @JsonKey(name: 'returnNumber') required String returnNumber,
    @JsonKey(name: 'customerDisplayName') required String customerDisplayName,
    @JsonKey(name: 'reason') required String reason,
    @JsonKey(name: 'voidReason') String? voidReason,
  }) = _CreditNotePrintDto;

  factory CreditNotePrintDto.fromJson(Map<String, dynamic> json) =>
      _$CreditNotePrintDtoFromJson(json);
}

@freezed
sealed class CreditNotesResponseDto with _$CreditNotesResponseDto {
  const factory CreditNotesResponseDto({
    @JsonKey(name: 'items') @Default([]) List<CreditNoteListItemDto> items,
    @JsonKey(name: 'totalCount') required int totalCount,
    @JsonKey(name: 'pageNumber') required int pageNumber,
    @JsonKey(name: 'pageSize') required int pageSize,
  }) = _CreditNotesResponseDto;

  factory CreditNotesResponseDto.fromJson(Map<String, dynamic> json) =>
      _$CreditNotesResponseDtoFromJson(json);
}
