import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  test('gets first 20 expenses and parses backend list contract', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);

    when(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 1, 'pageSize': 20},
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': [
            {
              'id': 'expense-2',
              'amount': 1250,
              'categoryName': 'Rent',
              'paidTo': 'Landlord',
              'expenseDate': '2026-07-02',
              'isVoided': false,
            },
            {
              'id': 'expense-1',
              'amount': 499.5,
              'categoryName': 'Utilities',
              'paidTo': 'Power Company',
              'expenseDate': '2026-07-01',
              'isVoided': true,
            },
          ],
          'totalCount': 2,
          'pageNumber': 1,
          'pageSize': 20,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/expenses'),
      ),
    );

    final page = await dataSource.getExpenses();

    expect(page.items.map((item) => item.id), ['expense-2', 'expense-1']);
    expect(page.items.first.amount, 1250.0);
    expect(page.items.first.expenseDate, DateTime(2026, 7, 2));
    expect(page.items.last.isVoided, isTrue);
    expect(page.totalCount, 2);
    expect(page.pageNumber, 1);
    expect(page.pageSize, 20);
    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 1, 'pageSize': 20},
      ),
    ).called(1);
  });

  test('omits whitespace search while retaining pagination query', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);

    when(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 3, 'pageSize': 40},
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': <dynamic>[],
          'totalCount': 0,
          'pageNumber': 3,
          'pageSize': 40,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/expenses'),
      ),
    );

    await dataSource.getExpenses(page: 3, pageSize: 40, search: '  ');

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 3, 'pageSize': 40},
      ),
    ).called(1);
  });

  test('trims non-empty search while retaining pagination query', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);

    when(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 2, 'pageSize': 15, 'search': 'rent'},
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': <dynamic>[],
          'totalCount': 0,
          'pageNumber': 2,
          'pageSize': 15,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/expenses'),
      ),
    );

    await dataSource.getExpenses(page: 2, pageSize: 15, search: '  rent  ');

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 2, 'pageSize': 15, 'search': 'rent'},
      ),
    ).called(1);
  });

  test('gets and parses every expense detail field', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);
    final responseData = <String, dynamic>{
      'id': 'expense-1',
      'shopId': 'shop-1',
      'categoryId': 'category-1',
      'categoryName': 'Rent',
      'amount': 1250.5,
      'paidTo': 'Landlord',
      'description': null,
      'expenseDate': '2026-07-01',
      'actorUserId': 'user-1',
      'isVoided': true,
      'originalExpenseId': 'expense-original',
      'supplierLedgerEntryId': 'ledger-1',
      'createdAt': '2026-07-01T08:30:00.000Z',
    };

    when(
      () => apiClient.get<Map<String, dynamic>>('/expenses/expense-1'),
    ).thenAnswer(
      (_) async => Response(
        data: responseData,
        statusCode: 200,
        requestOptions: RequestOptions(path: '/expenses/expense-1'),
      ),
    );

    final result = await dataSource.getExpenseDetail('expense-1');

    expect(result.id, 'expense-1');
    expect(result.description, isNull);
    expect(result.originalExpenseId, 'expense-original');
    expect(result.supplierLedgerEntryId, 'ledger-1');
    expect(result.isVoided, isTrue);
    expect(result.createdAt, DateTime.utc(2026, 7, 1, 8, 30));
    verify(
      () => apiClient.get<Map<String, dynamic>>('/expenses/expense-1'),
    ).called(1);
  });
}
