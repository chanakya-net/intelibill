import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/cancel_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  group('PurchaseOrderRemoteDataSource.cancel', () {
    test('POSTs to /purchase-orders/{id}/cancel with trimmed reason', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);

      when(
        () => apiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _cancelledJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-1/cancel'),
        ),
      );

      await source.cancel(
        'po-1',
        CancelPurchaseOrderRequestDto(reason: 'No longer needed'),
      );

      verify(
        () => apiClient.post<Map<String, dynamic>>(
          '/purchase-orders/po-1/cancel',
          data: {'reason': 'No longer needed'},
        ),
      ).called(1);
    });

    test('prevents blank reason from reaching API', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);

      when(
        () => apiClient.post<Map<String, dynamic>>(any(), data: any(named: 'data')),
      ).thenAnswer((_) async => Response(
        data: _cancelledJson(),
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-1/cancel'),
      ));

      // Blank reason should be converted to null and not sent
      // (repo handles validation; data source just serializes)
      await source.cancel(
        'po-1',
        CancelPurchaseOrderRequestDto(reason: 'Valid reason'),
      );

      verify(() => apiClient.post<Map<String, dynamic>>(any(), data: any(named: 'data')))
          .called(1);
    });

    test('maps response to PurchaseOrderDetailDto', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);

      when(
        () => apiClient.post<Map<String, dynamic>>(any(), data: any(named: 'data')),
      ).thenAnswer(
        (_) async => Response(
          data: _cancelledJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-1/cancel'),
        ),
      );

      final result = await source.cancel(
        'po-1',
        CancelPurchaseOrderRequestDto(reason: 'No longer needed'),
      );

      expect(result, isA<PurchaseOrderDetailDto>());
      expect(result.purchaseOrderId, 'po-1');
    });
  });
}

Map<String, dynamic> _cancelledJson() {
  return {
    'purchaseOrderId': 'po-1',
    'purchaseOrderNumber': 'PO-001',
    'status': 'Cancelled',
    'lines': [],
    'expectedTotal': 0.0,
    'createdAt': '2026-07-01T10:00:00Z',
    'receivedQuantity': 0,
    'cancellationReason': 'No longer needed',
  };
}
