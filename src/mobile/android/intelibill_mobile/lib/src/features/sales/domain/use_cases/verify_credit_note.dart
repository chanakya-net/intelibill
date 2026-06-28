import 'package:intelibill_mobile/src/features/sales/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class VerifyCreditNote {
  const VerifyCreditNote(this._repository);

  final SalesRepository _repository;

  Future<CreditNoteVerifyResult> call(String code) {
    return _repository.verifyCreditNote(code);
  }
}
