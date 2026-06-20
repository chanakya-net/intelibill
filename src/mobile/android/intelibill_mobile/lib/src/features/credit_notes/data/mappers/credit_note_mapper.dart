import 'package:intelibill_mobile/src/features/credit_notes/data/dto/credit_note_dto.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';

class CreditNoteMapper {
  static CreditNote toDomain(CreditNoteDto dto) {
    return CreditNote(
      creditNoteId: dto.creditNoteId,
      code: dto.code,
      status: dto.status,
      originalAmount: dto.originalAmount,
      availableBalance: dto.availableBalance,
      expiresAt: dto.expiresAt,
      isVoided: dto.isVoided,
      saleReturnId: dto.saleReturnId,
      reason: dto.reason,
      voidReason: dto.voidReason,
      returnNumber: dto.returnNumber,
      invoiceNumber: dto.invoiceNumber,
      customerName: dto.customerName,
    );
  }

  static CreditNotePrint printToDomain(CreditNotePrintDto dto) {
    return CreditNotePrint(
      creditNoteId: dto.creditNoteId,
      code: dto.code,
      status: dto.status,
      isUsable: dto.isUsable,
      originalAmount: dto.originalAmount,
      availableBalance: dto.availableBalance,
      issuedAt: dto.issuedAt,
      expiresAt: dto.expiresAt,
      saleId: dto.saleId,
      invoiceNumber: dto.invoiceNumber,
      saleReturnId: dto.saleReturnId,
      returnNumber: dto.returnNumber,
      customerDisplayName: dto.customerDisplayName,
      reason: dto.reason,
      voidReason: dto.voidReason,
    );
  }

  static CreditNotesResult listToDomain(CreditNotesResponseDto dto) {
    return CreditNotesResult(
      items: dto.items.map((item) {
        return CreditNote(
          creditNoteId: item.creditNoteId,
          code: item.code,
          status: item.status,
          originalAmount: item.originalAmount,
          availableBalance: item.availableBalance,
          expiresAt: item.expiresAt,
          isVoided: false,
          saleReturnId: item.saleReturnId,
          reason: '',
          voidReason: null,
          returnNumber: item.returnNumber,
          invoiceNumber: item.invoiceNumber,
          customerName: item.customerName,
        );
      }).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
    );
  }
}
