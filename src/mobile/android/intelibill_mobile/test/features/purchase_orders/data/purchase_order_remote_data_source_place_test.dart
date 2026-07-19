import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  group('PurchaseOrderRemoteDataSource.place', () {
    test('POSTs to /purchase-orders/{id}/place with empty body', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);

      when(
        () => apiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _placedJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-1/place'),
        ),
      );

      await source.place('po-1');

      verify(
        () => apiClient.post<Map<String, dynamic>>(
          '/purchase-orders/po-1/place',
          data: {},
        ),
      ).called(1);
    });

    test('maps placed DTO from response', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);

      when(
        () => apiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _placedJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-1/place'),
        ),
      );

      final result = await source.place('po-1');

      expect(result, isA<PurchaseOrderDetailDto>());
      expect(result.purchaseOrderId, 'po-1');
    });
  });
}

Map<String, dynamic> _placedJson() {
  return {
    'purchaseOrderId': 'po-1',
    'purchaseOrderNumber': 'PO-001',
    'status': 'Placed',
    'lines': <dynamic>[],
    'expectedTotal': 100.0,
    'createdAt': '2026-07-01T00:00:00Z',
    'receivedQuantity': 0,
  };
}
