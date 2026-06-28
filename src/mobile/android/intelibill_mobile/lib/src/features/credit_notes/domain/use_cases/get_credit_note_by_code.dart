import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/repositories/credit_note_repository.dart';

class GetCreditNoteByCode {
  const GetCreditNoteByCode(this._repository);

  final CreditNoteRepository _repository;

  Future<CreditNote> call(String code) {
    return _repository.getCreditNoteByCode(code);
  }
}
