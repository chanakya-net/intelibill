import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/mappers/expense_mapper.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';

class ExpenseRepositoryImpl implements ExpenseRepository {
  const ExpenseRepositoryImpl(this._remoteDataSource);

  final ExpenseRemoteDataSource _remoteDataSource;

  @override
  Future<ExpensesPage> getExpenses() async {
    try {
      final dto = await _remoteDataSource.getExpenses();
      return ExpenseMapper.pageToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } on Object catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
