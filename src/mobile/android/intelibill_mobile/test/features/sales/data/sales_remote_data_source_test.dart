import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late SalesRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = SalesRemoteDataSourceImpl(mockApiClient);
  });

  group('SalesRemoteDataSourceImpl', () {
    test('calls /sales with history query params', () async {
      final from = DateTime(2026, 5);
      final to = DateTime(2026, 5, 12, 23, 59, 59);

      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'items': <dynamic>[],
            'totalCount': 0,
            'pageNumber': 2,
            'pageSize': 25,
            'summary': {
              'periodSales': 0,
              'invoiceCount': 0,
              'refundAmount': 0,
            },
          },
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales'),
        ),
      );

      final response = await remoteDataSource.getSalesHistory(
        SalesHistoryQuery(
          from: from,
          to: to,
          search: 'john',
          status: 'paid',
          page: 2,
          pageSize: 25,
        ),
      );

      expect(response.totalCount, 0);
      verify(
        () => mockApiClient.get<Map<String, dynamic>>(
          '/sales',
          queryParameters: {
            'from': '2026-05-01',
            'to': '2026-05-12',
            'search': 'john',
            'status': 'paid',
            'page': 2,
            'pageSize': 25,
          },
        ),
      ).called(1);
    });

    test('throws typed error when sale detail body is null', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>?>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response<Map<String, dynamic>>(
          data: null,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/sale-1'),
        ),
      );

      await expectLater(
        remoteDataSource.getSaleDetail('sale-1'),
        throwsA(
          isA<AppException>().having(
            (error) => error.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });
  });
}
