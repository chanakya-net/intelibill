import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/close_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  group('PurchaseOrderRemoteDataSource.close', () {
    test('POSTs the exact close path and trimmed reason', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
      when(
        () => apiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _closedJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-1/close'),
        ),
      );

      final result = await source.close(
        'po-1',
        const ClosePurchaseOrderRequestDto(reason: '  Discontinued  '),
      );

      expect(result, isA<PurchaseOrderDetailDto>());
      verify(
        () => apiClient.post<Map<String, dynamic>>(
          '/purchase-orders/po-1/close',
          data: {'reason': 'Discontinued'},
        ),
      ).called(1);
    });

    test('rejects blank and overlong reasons without an API call', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);

      expect(
        () => source.close(
          'po-1',
          const ClosePurchaseOrderRequestDto(reason: '   '),
        ),
        throwsA(isA<AppException>()),
      );
      expect(
        () => source.close(
          'po-1',
          ClosePurchaseOrderRequestDto(reason: 'x' * 501),
        ),
        throwsA(isA<AppException>()),
      );
      verifyNever(
        () => apiClient.post<Map<String, dynamic>>(
          any(),
          data: any<dynamic>(named: 'data'),
        ),
      );
    });

    test('maps server closure metadata from the detail response', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
      when(
        () => apiClient.post<Map<String, dynamic>>(
          any(),
          data: any<dynamic>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _closedJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-1/close'),
        ),
      );

      final result = await source.close(
        'po-1',
        const ClosePurchaseOrderRequestDto(reason: 'Discontinued'),
      );

      expect(result.status, 'Closed');
      expect(result.closeReason, 'Server reason');
      expect(result.closedBy, 'user-1');
      expect(result.closedAt, '2026-07-15T10:30:00Z');
    });
  });
}

Map<String, dynamic> _closedJson() => {
  'purchaseOrderId': 'po-1',
  'purchaseOrderNumber': 'PO-001',
  'status': 'Closed',
  'lines': <dynamic>[],
  'expectedTotal': 0.0,
  'createdAt': '2026-07-01T10:00:00Z',
  'receivedQuantity': 5,
  'closedAt': '2026-07-15T10:30:00Z',
  'closedBy': 'user-1',
  'closeReason': 'Server reason',
};
