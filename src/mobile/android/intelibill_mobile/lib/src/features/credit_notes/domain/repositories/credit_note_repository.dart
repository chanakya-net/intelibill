import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';

interface class CreditNoteRepository {
  Future<CreditNotesResult> getCreditNotes(CreditNotesQuery query) {
    throw UnimplementedError();
  }

  Future<CreditNote> getCreditNoteByCode(String code) {
    throw UnimplementedError();
  }

  Future<CreditNotePrint> getCreditNotePrintByCode(String code) {
    throw UnimplementedError();
  }
}
