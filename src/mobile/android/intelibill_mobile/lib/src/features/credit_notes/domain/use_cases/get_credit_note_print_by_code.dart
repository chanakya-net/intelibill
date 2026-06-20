import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/repositories/credit_note_repository.dart';

class GetCreditNotePrintByCode {
  const GetCreditNotePrintByCode(this._repository);

  final CreditNoteRepository _repository;

  Future<CreditNotePrint> call(String code) {
    return _repository.getCreditNotePrintByCode(code);
  }
}
