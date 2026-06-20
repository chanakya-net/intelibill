import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
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
    test('returns sale detail map on success', () async {
      final responseBody = <String, dynamic>{
        'saleId': 'sale-1',
        'invoiceNumber': 'INV-2026-001',
        'paymentMethod': 1,
        'soldAt': '2026-05-11T10:00:00.000Z',
        'paidAmount': 500.0,
        'dueAmount': 0.0,
        'totalBeforeDiscount': 500.0,
        'totalDiscountAmount': 0.0,
        'totalAmount': 500.0,
        'totalTaxAmount': 50.0,
        'creditNoteAppliedAmount': 0.0,
        'dueReductionAmount': 0.0,
      };

      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>?>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response<Map<String, dynamic>>(
          data: responseBody,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/sale-1'),
        ),
      );

      final response = await remoteDataSource.getSaleDetail('sale-1');
      expect(response, responseBody);
    });

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

    test('SaleDetailDto.fromJson parses real backend SaleDto contract', () {
      final backendPayload = <String, dynamic>{
        'saleId': 'a1b2c3d4-0000-0000-0000-000000000001',
        'invoiceNumber': 'INV-2026-042',
        'customerId': null,
        'customerName': 'Jane Doe',
        'customerPhone': '9876543210',
        'paymentMethod': 1,
        'soldAt': '2026-05-20T14:30:00.000Z',
        'paidAmount': 590.0,
        'dueAmount': 0.0,
        'totalBeforeDiscount': 600.0,
        'totalDiscountAmount': 10.0,
        'totalAmount': 590.0,
        'totalTaxAmount': 90.0,
        'creditNoteAppliedAmount': 0.0,
        'items': [
          {
            'saleItemId': 'b1b2c3d4-0000-0000-0000-000000000002',
            'itemName': 'Widget A',
            'quantity': 2.0,
            'salesPrice': 250.0,
            'taxRatePercent': 18.0,
            'totalAmount': 590.0,
          },
        ],
        'returns': [
          {
            'saleReturnId': 'c1b2c3d4-0000-0000-0000-000000000003',
            'returnNumber': 'RET-001',
            'totalRefundAmount': 118.0,
            'processedAt': '2026-05-21T10:00:00.000Z',
            'items': [
              {
                'saleItemId': 'b1b2c3d4-0000-0000-0000-000000000002',
                'quantity': 1.0,
                'approvedRefundAmount': 118.0,
              },
            ],
          },
        ],
        'creditNoteRedemptions': [
          {
            'creditNoteId': 'd1b2c3d4-0000-0000-0000-000000000004',
            'code': 'CN-ABC-001',
            'appliedAmount': 50.0,
          },
        ],
        'warnings': ['Stock level low for Widget A'],
      };

      final dto = SaleDetailDto.fromJson(backendPayload);
      expect(dto.saleId, 'a1b2c3d4-0000-0000-0000-000000000001');
      expect(dto.invoiceNumber, 'INV-2026-042');
      expect(dto.status, isNull);
      expect(dto.items, hasLength(1));
      expect(dto.items.first.itemId, 'b1b2c3d4-0000-0000-0000-000000000002');
      expect(dto.items.first.name, 'Widget A');
      expect(dto.items.first.rate, 250.0);
      expect(dto.items.first.tax, 18.0);
      expect(dto.items.first.total, 590.0);
      expect(dto.returns, hasLength(1));
      expect(
        dto.returns.first.returnId,
        'c1b2c3d4-0000-0000-0000-000000000003',
      );
      expect(dto.returns.first.amount, 118.0);
      expect(dto.returns.first.items.first.amount, 118.0);
      expect(dto.redemptions, hasLength(1));
      expect(dto.redemptions.first.code, 'CN-ABC-001');
      expect(dto.redemptions.first.amount, 50.0);
      expect(dto.warnings, equals(['Stock level low for Widget A']));
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
