import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/data_sources/bank_accounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/mappers/bank_account_mapper.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/repositories/bank_accounts_repository.dart';

class BankAccountsRepositoryImpl implements BankAccountsRepository {
  const BankAccountsRepositoryImpl(this._remoteDataSource);

  final BankAccountsRemoteDataSource _remoteDataSource;

  @override
  Future<List<BankAccount>> getBankAccounts() async {
    try {
      final dtos = await _remoteDataSource.getBankAccounts();
      return dtos.map(BankAccountMapper.toDomain).toList();
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<void> addBankAccount(SaveBankAccountRequest request) async {
    try {
      await _remoteDataSource.addBankAccount(
        BankAccountMapper.toSaveDto(request),
      );
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
