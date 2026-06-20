import 'package:intelibill_mobile/src/features/credit_notes/domain/repositories/credit_note_repository.dart';

class VoidCreditNote {
  const VoidCreditNote(this._repository);

  final CreditNoteRepository _repository;

  Future<void> call(String code, {required String reason}) {
    return _repository.voidCreditNote(code: code, reason: reason);
  }
}
