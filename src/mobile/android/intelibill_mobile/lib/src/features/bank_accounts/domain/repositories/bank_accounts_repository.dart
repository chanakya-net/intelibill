import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';

interface class BankAccountsRepository {
  Future<List<BankAccount>> getBankAccounts() {
    throw UnimplementedError();
  }

  Future<void> addBankAccount(SaveBankAccountRequest request) {
    throw UnimplementedError();
  }

  Future<void> updateBankAccount(String id, SaveBankAccountRequest request) {
    throw UnimplementedError();
  }

  Future<void> deleteBankAccount(String id) {
    throw UnimplementedError();
  }
}
