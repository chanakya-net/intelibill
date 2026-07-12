import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/dto/bank_account_dto.dart';

interface class BankAccountsRemoteDataSource {
  Future<List<BankAccountDto>> getBankAccounts() {
    throw UnimplementedError();
  }
}

class BankAccountsRemoteDataSourceImpl implements BankAccountsRemoteDataSource {
  BankAccountsRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  @override
  Future<List<BankAccountDto>> getBankAccounts() async {
    final response = await _apiClient.get<List<dynamic>>('/bank-accounts');
    return response.data!
        .cast<Map<String, dynamic>>()
        .map(BankAccountDto.fromJson)
        .toList();
  }
}
