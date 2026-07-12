import 'package:intelibill_mobile/src/features/bank_accounts/domain/repositories/bank_accounts_repository.dart';

class DeleteBankAccount {
  const DeleteBankAccount(this._repository);

  final BankAccountsRepository _repository;

  Future<void> call(String id) => _repository.deleteBankAccount(id);
}
