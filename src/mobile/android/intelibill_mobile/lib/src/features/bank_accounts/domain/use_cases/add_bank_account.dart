import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/repositories/bank_accounts_repository.dart';

class AddBankAccount {
  const AddBankAccount(this._repository);

  final BankAccountsRepository _repository;

  Future<void> call(SaveBankAccountRequest request) {
    return _repository.addBankAccount(request);
  }
}
