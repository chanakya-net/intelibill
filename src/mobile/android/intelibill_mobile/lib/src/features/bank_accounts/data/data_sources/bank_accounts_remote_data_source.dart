import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/dto/bank_account_dto.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/dto/save_bank_account_request_dto.dart';

interface class BankAccountsRemoteDataSource {
  Future<List<BankAccountDto>> getBankAccounts() {
    throw UnimplementedError();
  }

  Future<void> addBankAccount(SaveBankAccountRequestDto request) {
    throw UnimplementedError();
  }

  Future<void> updateBankAccount(String id, SaveBankAccountRequestDto request) {
    throw UnimplementedError();
  }

  Future<void> deleteBankAccount(String id) {
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

  @override
  Future<void> addBankAccount(SaveBankAccountRequestDto request) async {
    await _apiClient.post<void>('/bank-accounts', data: request.toJson());
  }

  @override
  Future<void> updateBankAccount(
    String id,
    SaveBankAccountRequestDto request,
  ) async {
    await _apiClient.put<void>('/bank-accounts/$id', data: request.toJson());
  }

  @override
  Future<void> deleteBankAccount(String id) async {
    await _apiClient.delete<void>('/bank-accounts/$id');
  }
}
