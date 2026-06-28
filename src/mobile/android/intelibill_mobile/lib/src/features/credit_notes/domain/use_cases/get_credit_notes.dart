import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/repositories/credit_note_repository.dart';

class GetCreditNotes {
  const GetCreditNotes(this._repository);

  final CreditNoteRepository _repository;

  Future<CreditNotesResult> call(CreditNotesQuery query) {
    return _repository.getCreditNotes(query);
  }
}
