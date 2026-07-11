import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_mutation_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  test('gets expense categories', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.get<List<dynamic>>('/expenses/categories'),
    ).thenAnswer(
      (_) async => Response(
        data: [
          {'id': 'rent', 'name': 'Rent'},
          {'id': 'utilities', 'name': 'Utilities'},
        ],
        statusCode: 200,
        requestOptions: RequestOptions(path: '/expenses/categories'),
      ),
    );

    final categories = await dataSource.getCategories();

    expect(categories.map((category) => category.name), ['Rent', 'Utilities']);
    verify(
      () => apiClient.get<List<dynamic>>('/expenses/categories'),
    ).called(1);
  });

  test('records an expense with the backend payload contract', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.post<Map<String, dynamic>>(
        '/expenses',
        data: {
          'categoryName': 'Rent',
          'amount': 1250.5,
          'paidTo': 'Landlord',
          'description': null,
          'expenseDate': '2026-07-02',
        },
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'id': 'expense-1',
          'shopId': 'shop-1',
          'categoryId': 'rent',
          'categoryName': 'Rent',
          'amount': 1250.5,
          'paidTo': 'Landlord',
          'description': null,
          'expenseDate': '2026-07-02',
          'actorUserId': 'user-1',
          'isVoided': false,
          'originalExpenseId': null,
          'supplierLedgerEntryId': null,
          'createdAt': '2026-07-02T08:30:00.000Z',
        },
        statusCode: 201,
        requestOptions: RequestOptions(path: '/expenses'),
      ),
    );

    final expense = await dataSource.recordExpense(
      ExpenseMutationRequestDto(
        categoryName: '  Rent ',
        amount: 1250.5,
        paidTo: ' Landlord ',
        description: '  ',
        expenseDate: DateTime(2026, 7, 2),
      ),
    );

    expect(expense.id, 'expense-1');
    verify(
      () => apiClient.post<Map<String, dynamic>>(
        '/expenses',
        data: {
          'categoryName': 'Rent',
          'amount': 1250.5,
          'paidTo': 'Landlord',
          'description': null,
          'expenseDate': '2026-07-02',
        },
      ),
    ).called(1);
  });

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

  test('corrects an expense with the backend payload contract', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.post<Map<String, dynamic>>(
        '/expenses/expense-1/correct',
        data: {
          'categoryName': 'Utilities',
          'amount': 150.0,
          'paidTo': 'Power Co',
          'description': null,
          'expenseDate': '2026-07-03',
        },
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'id': 'expense-2',
          'shopId': 'shop-1',
          'categoryId': 'utilities',
          'categoryName': 'Utilities',
          'amount': 150.0,
          'paidTo': 'Power Co',
          'description': null,
          'expenseDate': '2026-07-03',
          'actorUserId': 'user-1',
          'isVoided': false,
          'originalExpenseId': 'expense-1',
          'supplierLedgerEntryId': null,
          'createdAt': '2026-07-03T08:30:00.000Z',
        },
        statusCode: 201,
        requestOptions: RequestOptions(path: '/expenses/expense-1/correct'),
      ),
    );

    final expense = await dataSource.correctExpense(
      'expense-1',
      ExpenseMutationRequestDto(
        categoryName: '  Utilities ',
        amount: 150,
        paidTo: ' Power Co ',
        description: '  ',
        expenseDate: DateTime(2026, 7, 3),
      ),
    );

    expect(expense.id, 'expense-2');
    expect(expense.originalExpenseId, 'expense-1');
    verify(
      () => apiClient.post<Map<String, dynamic>>(
        '/expenses/expense-1/correct',
        data: {
          'categoryName': 'Utilities',
          'amount': 150.0,
          'paidTo': 'Power Co',
          'description': null,
          'expenseDate': '2026-07-03',
        },
      ),
    ).called(1);
  });
}
