import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/repositories/bank_accounts_repository.dart';

class GetBankAccounts {
  const GetBankAccounts(this._repository);

  final BankAccountsRepository _repository;

  Future<List<BankAccount>> call() => _repository.getBankAccounts();
}
