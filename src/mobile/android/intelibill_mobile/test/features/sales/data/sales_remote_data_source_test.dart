import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
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

    test('calls /sales/{saleId} for sale detail', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          '/sales/sale-abc',
        ),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'saleId': 'sale-abc',
            'invoiceNumber': 'INV-001',
            'paymentMethod': 1,
            'soldAt': '2026-05-11T10:30:00.000Z',
            'paidAmount': 0.0,
            'dueAmount': 0.0,
            'totalBeforeDiscount': 0.0,
            'totalDiscountAmount': 0.0,
            'totalAmount': 0.0,
            'totalTaxAmount': 0.0,
          },
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/sale-abc'),
        ),
      );

      final response = await remoteDataSource.getSaleDetail('sale-abc');

      expect(response.saleId, 'sale-abc');
      verify(
        () => mockApiClient.get<Map<String, dynamic>>(
          '/sales/sale-abc',
        ),
      ).called(1);
    });

    test('calls preview return endpoint with request map', () async {
      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/sales/sale-abc/returns/preview',
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'saleId': 'sale-abc',
            'hasFinancialAccess': true,
            'lines': [
              {
                'saleItemId': 'item-1',
                'requestedQuantity': 1.0,
                'returnedQuantity': 1.0,
                'returnableQuantity': 2.0,
                'condition': 1,
                'willRestock': true,
                'financial': {
                  'originalCostPrice': 60.0,
                  'originalSalesPrice': 100.0,
                  'originalTaxRatePercent': 18.0,
                  'originalIsPriceIncludingTax': false,
                  'maxRefundAmount': 100.0,
                  'approvedRefundAmount': 90.0,
                  'taxableAmount': 80.0,
                  'taxAmount': 14.4,
                },
              },
            ],
            'financial': {
              'totalRefundAmount': 90.0,
              'dueReductionAmount': 0.0,
              'payoutAmount': 90.0,
              'totalTaxableAmount': 80.0,
              'totalTaxAmount': 14.4,
            },
            'warnings': [
              {
                'code': 'R01',
                'message': 'Stock may need adjustment.',
                'severity': 'info',
              },
            ],
          },
          statusCode: 200,
          requestOptions: RequestOptions(
            path: '/sales/sale-abc/returns/preview',
          ),
        ),
      );

      final response = await remoteDataSource.previewSaleReturn(
        saleId: 'sale-abc',
        request: {
          'dueReductionOverrideAmount': 5.0,
          'items': [
            {
              'saleItemId': 'item-1',
              'lineType': 'Goods',
              'quantity': 1.0,
              'condition': 1,
              'approvedRefundAmount': 90.0,
            },
          ],
        },
      );

      expect(response.saleId, 'sale-abc');
      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/sales/sale-abc/returns/preview',
          data: {
            'dueReductionOverrideAmount': 5.0,
            'items': [
              {
                'saleItemId': 'item-1',
                'lineType': 'Goods',
                'quantity': 1.0,
                'condition': 1,
                'approvedRefundAmount': 90.0,
              },
            ],
          },
        ),
      ).called(1);
    });

    test('calls /sales/preview with mixed goods and service lines', () async {
      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/sales/preview',
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _previewResponseJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/preview'),
        ),
      );

      final response = await remoteDataSource.previewSale(
        request: {
          'saleDiscount': {'type': 0, 'value': 0},
          'items': [
            {
              'inventoryBatchId': 'batch-1',
              'barcode': 'BAR-1',
              'batchNumber': 'BN-1',
              'itemName': 'Rice',
              'quantity': 2.0,
              'costPrice': 48.0,
              'salesPrice': 60.0,
              'mrp': 62.0,
              'taxRatePercent': 12.0,
              'isPriceIncludingTax': false,
              'itemDiscount': {'type': 0, 'value': 0},
              'clientLineKey': 'line-1',
              'lineType': 'Goods',
            },
            {
              'inventoryBatchId': '00000000-0000-0000-0000-000000000000',
              'barcode': 'SRV-1',
              'batchNumber': '',
              'itemName': 'Installation',
              'quantity': 1.0,
              'costPrice': 0.0,
              'salesPrice': 150.0,
              'mrp': 150.0,
              'taxRatePercent': 18.0,
              'isPriceIncludingTax': false,
              'itemDiscount': {'type': 0, 'value': 0},
              'clientLineKey': 'line-2',
              'lineType': 'Service',
              'serviceId': 'svc-1',
            },
          ],
        },
      );

      expect(response.totalAmount, 236.0);
      expect(response.lines, hasLength(2));
      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/sales/preview',
          data: {
            'saleDiscount': {'type': 0, 'value': 0},
            'items': [
              {
                'inventoryBatchId': 'batch-1',
                'barcode': 'BAR-1',
                'batchNumber': 'BN-1',
                'itemName': 'Rice',
                'quantity': 2.0,
                'costPrice': 48.0,
                'salesPrice': 60.0,
                'mrp': 62.0,
                'taxRatePercent': 12.0,
                'isPriceIncludingTax': false,
                'itemDiscount': {'type': 0, 'value': 0},
                'clientLineKey': 'line-1',
                'lineType': 'Goods',
              },
              {
                'inventoryBatchId': '00000000-0000-0000-0000-000000000000',
                'barcode': 'SRV-1',
                'batchNumber': '',
                'itemName': 'Installation',
                'quantity': 1.0,
                'costPrice': 0.0,
                'salesPrice': 150.0,
                'mrp': 150.0,
                'taxRatePercent': 18.0,
                'isPriceIncludingTax': false,
                'itemDiscount': {'type': 0, 'value': 0},
                'clientLineKey': 'line-2',
                'lineType': 'Service',
                'serviceId': 'svc-1',
              },
            ],
          },
        ),
      ).called(1);
    });

    test('calls record return endpoint with request map', () async {
      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/sales/sale-abc/returns',
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'saleId': 'sale-abc',
            'invoiceNumber': 'INV-001',
            'paymentMethod': 1,
            'soldAt': '2026-05-11T10:30:00.000Z',
            'paidAmount': 0.0,
            'dueAmount': 0.0,
            'totalBeforeDiscount': 0.0,
            'totalDiscountAmount': 0.0,
            'totalAmount': 0.0,
            'totalTaxAmount': 0.0,
          },
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/sale-abc/returns'),
        ),
      );

      await remoteDataSource.recordSaleReturn(
        saleId: 'sale-abc',
        request: {
          'payoutDestination': 2,
          'dueReductionOverrideAmount': 5.0,
          'items': const [
            {
              'saleItemId': 'item-1',
              'quantity': 1.0,
              'approvedRefundAmount': 90.0,
            },
          ],
        },
      );

      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/sales/sale-abc/returns',
          data: {
            'payoutDestination': 2,
            'dueReductionOverrideAmount': 5.0,
            'items': const [
              {
                'saleItemId': 'item-1',
                'quantity': 1.0,
                'approvedRefundAmount': 90.0,
              },
            ],
          },
        ),
      ).called(1);
    });

    test('calls /sales/sellables with searchTerm query', () async {
      when(
        () => mockApiClient.get<List<dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: <dynamic>[],
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/sellables'),
        ),
      );

      await remoteDataSource.searchSellables(searchTerm: 'flour');

      verify(
        () => mockApiClient.get<List<dynamic>>(
          '/sales/sellables',
          queryParameters: {'searchTerm': 'flour'},
        ),
      ).called(1);
    });

    test('calls /sales/sellables with barcode query', () async {
      when(
        () => mockApiClient.get<List<dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: <dynamic>[],
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/sellables'),
        ),
      );

      await remoteDataSource.searchSellables(barcode: 'BAR001');

      verify(
        () => mockApiClient.get<List<dynamic>>(
          '/sales/sellables',
          queryParameters: {'barcode': 'BAR001'},
        ),
      ).called(1);
    });

    test('voids a sale return with reason payload', () async {
      when(
        () => mockApiClient.post<void>(
          '/sales/returns/return-1/void',
          data: {'reason': 'Damaged'},
        ),
      ).thenAnswer(
        (_) async => Response<void>(
          data: null,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/sales/returns/return-1/void'),
        ),
      );

      await remoteDataSource.voidSaleReturn(
        saleReturnId: 'return-1',
        reason: 'Damaged',
      );

      verify(
        () => mockApiClient.post<void>(
          '/sales/returns/return-1/void',
          data: {'reason': 'Damaged'},
        ),
      ).called(1);
    });
  });
}

Map<String, dynamic> _previewResponseJson() => {
  'totalAmount': 236.0,
  'totalTaxableAmount': 200.0,
  'totalTaxAmount': 36.0,
  'totalDiscountAmount': 14.0,
  'saleLevelEligibleSubtotal': 120.0,
  'configuredSaleRule': {
    'ruleId': 'rule-1',
    'ruleType': 'SalePercentage',
    'percentage': 10.0,
    'thresholdAmount': 100.0,
  },
  'lines': [
    {
      'lineType': 'Goods',
      'itemId': 'item-1',
      'serviceId': null,
      'barcode': 'BAR-1',
      'itemName': 'Rice',
      'inventoryBatchId': 'batch-1',
      'batchNumber': 'BN-1',
      'quantity': 2.0,
      'costPrice': 48.0,
      'salesPrice': 60.0,
      'mrp': 62.0,
      'taxRatePercent': 12.0,
      'isPriceIncludingTax': false,
      'preTaxAmountBeforeDiscount': 120.0,
      'itemDiscountAmount': 0.0,
      'saleDiscountAmount': 4.0,
      'taxableAmount': 116.0,
      'taxAmount': 13.92,
      'lineTotalAmount': 129.92,
      'maxAllowedItemDiscountFlat': 12.0,
      'maxAllowedItemDiscountPercent': 10.0,
      'configuredBatchRuleId': 'batch-rule-1',
      'configuredBatchRulePercentage': 5.0,
      'hasClientPriceMismatch': true,
      'clientLineKey': 'line-1',
    },
    {
      'lineType': 'Service',
      'itemId': null,
      'serviceId': 'svc-1',
      'barcode': 'SRV-1',
      'itemName': 'Installation',
      'inventoryBatchId': null,
      'batchNumber': null,
      'quantity': 1.0,
      'costPrice': 0.0,
      'salesPrice': 150.0,
      'mrp': 150.0,
      'taxRatePercent': 18.0,
      'isPriceIncludingTax': false,
      'preTaxAmountBeforeDiscount': 150.0,
      'itemDiscountAmount': 0.0,
      'saleDiscountAmount': 10.0,
      'taxableAmount': 140.0,
      'taxAmount': 25.2,
      'lineTotalAmount': 165.2,
      'maxAllowedItemDiscountFlat': 0.0,
      'maxAllowedItemDiscountPercent': 0.0,
      'configuredBatchRuleId': null,
      'configuredBatchRulePercentage': null,
      'hasClientPriceMismatch': false,
      'clientLineKey': 'line-2',
    },
  ],
  'infos': [
    {
      'code': 'sale_preview.info.configured_rule_applied',
      'message': 'Configured sale rule applied.',
    },
  ],
  'warnings': [
    {
      'code': 'sale_preview.warning.client_price_mismatch',
      'message': 'Client pricing differs from latest batch pricing.',
      'severity': 'warning',
      'inventoryBatchId': 'batch-1',
      'clientLineKey': 'line-1',
    },
  ],
};
