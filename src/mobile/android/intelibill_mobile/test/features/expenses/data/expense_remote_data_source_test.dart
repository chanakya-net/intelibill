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
}
