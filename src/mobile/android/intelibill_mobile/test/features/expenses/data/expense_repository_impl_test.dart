import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_detail_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/repositories/expense_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class _MockExpenseRemoteDataSource extends Mock
    implements ExpenseRemoteDataSource {}

void main() {
  late _MockExpenseRemoteDataSource remoteDataSource;
  late ExpenseRepositoryImpl repository;

  setUp(() {
    remoteDataSource = _MockExpenseRemoteDataSource();
    repository = ExpenseRepositoryImpl(remoteDataSource);
  });

  test('maps every expense detail field to domain', () async {
    when(
      () => remoteDataSource.getExpenseDetail('expense-1'),
    ).thenAnswer((_) async => _detailDto());

    final detail = await repository.getExpenseDetail('expense-1');

    expect(detail.id, 'expense-1');
    expect(detail.shopId, 'shop-1');
    expect(detail.categoryId, 'category-1');
    expect(detail.categoryName, 'Rent');
    expect(detail.amount, 1250.5);
    expect(detail.paidTo, 'Landlord');
    expect(detail.description, 'July rent');
    expect(detail.expenseDate, DateTime(2026, 7));
    expect(detail.actorUserId, 'user-1');
    expect(detail.isVoided, isFalse);
    expect(detail.originalExpenseId, 'expense-original');
    expect(detail.supplierLedgerEntryId, 'ledger-1');
    expect(detail.createdAt, DateTime.utc(2026, 7, 1, 8, 30).toLocal());
  });

  test('preserves typed detail failures', () async {
    const failure = Failure.network(message: 'offline');
    when(
      () => remoteDataSource.getExpenseDetail('expense-1'),
    ).thenThrow(AppException(failure: failure));

    await expectLater(
      repository.getExpenseDetail('expense-1'),
      throwsA(
        isA<AppException>().having(
          (error) => error.failure,
          'failure',
          failure,
        ),
      ),
    );
  });
}

ExpenseDetailDto _detailDto() {
  return ExpenseDetailDto(
    id: 'expense-1',
    shopId: 'shop-1',
    categoryId: 'category-1',
    categoryName: 'Rent',
    amount: 1250.5,
    paidTo: 'Landlord',
    description: 'July rent',
    expenseDate: DateTime(2026, 7),
    actorUserId: 'user-1',
    isVoided: false,
    originalExpenseId: 'expense-original',
    supplierLedgerEntryId: 'ledger-1',
    createdAt: DateTime.utc(2026, 7, 1, 8, 30),
  );
}
