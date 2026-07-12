import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';

interface class BankAccountsRepository {
  Future<List<BankAccount>> getBankAccounts() {
    throw UnimplementedError();
  }
}
