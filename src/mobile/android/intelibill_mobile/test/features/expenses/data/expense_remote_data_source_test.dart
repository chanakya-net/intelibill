import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  test('gets the first 20 expenses and parses the list contract', () async {
    final apiClient = MockApiClient();
    final dataSource = ExpenseRemoteDataSourceImpl(apiClient);
    final responseData = <String, dynamic>{
      'items': [
        {
          'id': 'expense-1',
          'amount': 1250,
          'categoryName': 'Rent',
          'paidTo': 'Landlord',
          'expenseDate': '2026-07-01',
          'isVoided': false,
        },
        {
          'id': 'expense-2',
          'amount': 42.5,
          'categoryName': 'Travel',
          'paidTo': 'Metro',
          'expenseDate': '2026-06-30',
          'isVoided': true,
        },
      ],
      'totalCount': 22,
      'pageNumber': 1,
      'pageSize': 20,
    };

    when(
      () => apiClient.get<Map<String, dynamic>>(
        any(),
        queryParameters: any(named: 'queryParameters'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: responseData,
        statusCode: 200,
        requestOptions: RequestOptions(path: '/expenses'),
      ),
    );

    final result = await dataSource.getExpenses();

    expect(result.items.map((item) => item.id), ['expense-1', 'expense-2']);
    expect(result.items.first.amount, 1250.0);
    expect(result.items.first.expenseDate, DateTime(2026, 7));
    expect(result.items.last.amount, 42.5);
    expect(result.items.last.isVoided, isTrue);
    expect(result.totalCount, 22);
    expect(result.pageNumber, 1);
    expect(result.pageSize, 20);
    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/expenses',
        queryParameters: {'page': 1, 'pageSize': 20},
      ),
    ).called(1);
  });
}
