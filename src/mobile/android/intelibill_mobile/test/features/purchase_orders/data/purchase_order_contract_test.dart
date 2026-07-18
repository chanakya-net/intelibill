import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/mappers/purchase_order_mapper.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  group('PurchaseOrderStatus', () {
    for (final entry in PurchaseOrderStatus.values) {
      test('parses ${entry.wireValue}', () {
        expect(PurchaseOrderStatus.fromWire(entry.wireValue), entry);
      });
    }

    test('rejects an unknown wire value', () {
      expect(
        () => PurchaseOrderStatus.fromWire('Archived'),
        throwsFormatException,
      );
    });
  });

  group('purchase-order DTOs', () {
    test('deserializes confirmed list fields', () {
      final dto = PurchaseOrderListItemDto.fromJson(_itemJson());

      expect(dto.purchaseOrderId, 'po-1');
      expect(dto.status, 'PartiallyReceived');
      expect(dto.expectedTotal, 1240.5);
      expect(dto.createdAt, DateTime.utc(2026, 7, 1, 10));
    });

    test('deserializes a page', () {
      final page = PurchaseOrderPageDto.fromJson({
        'items': [_itemJson()],
        'totalCount': 31,
        'pageNumber': 1,
        'pageSize': 20,
      });

      expect(page.items, hasLength(1));
      expect(page.totalCount, 31);
      expect(page.pageNumber, 1);
      expect(page.pageSize, 20);
    });

    test('mapper rejects an invalid status', () {
      final dto = PurchaseOrderListItemDto.fromJson(
        _itemJson()..['status'] = 'Archived',
      );

      expect(() => PurchaseOrderMapper.toDomain(dto), throwsFormatException);
    });
  });

  test('GET /purchase-orders requests first 20-row page', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.get<Map<String, dynamic>>(
        any<String>(),
        queryParameters: any<Map<String, dynamic>>(named: 'queryParameters'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': <dynamic>[],
          'totalCount': 0,
          'pageNumber': 1,
          'pageSize': 20,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders'),
      ),
    );

    await source.getPurchaseOrders(const PurchaseOrderFilters());

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {'page': 1, 'page_size': 20},
      ),
    ).called(1);
  });

  test('omits blank search from query params', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.get<Map<String, dynamic>>(
        any<String>(),
        queryParameters: any<Map<String, dynamic>>(named: 'queryParameters'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': <dynamic>[],
          'totalCount': 0,
          'pageNumber': 1,
          'pageSize': 20,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders'),
      ),
    );

    await source.getPurchaseOrders(
      const PurchaseOrderFilters(search: ''),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {'page': 1, 'page_size': 20},
      ),
    ).called(1);
  });

  test('omits whitespace-only search from query params', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.get<Map<String, dynamic>>(
        any<String>(),
        queryParameters: any<Map<String, dynamic>>(named: 'queryParameters'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': <dynamic>[],
          'totalCount': 0,
          'pageNumber': 1,
          'pageSize': 20,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders'),
      ),
    );

    await source.getPurchaseOrders(
      const PurchaseOrderFilters(search: '   '),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {'page': 1, 'page_size': 20},
      ),
    ).called(1);
  });

  test('sends trimmed search under exactly `search` key', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.get<Map<String, dynamic>>(
        any<String>(),
        queryParameters: any<Map<String, dynamic>>(named: 'queryParameters'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: {
          'items': <dynamic>[],
          'totalCount': 0,
          'pageNumber': 1,
          'pageSize': 20,
        },
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders'),
      ),
    );

    await source.getPurchaseOrders(
      const PurchaseOrderFilters(search: '  widget  '),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {'page': 1, 'page_size': 20, 'search': 'widget'},
      ),
    ).called(1);
  });
}

Map<String, dynamic> _itemJson() => {
  'purchaseOrderId': 'po-1',
  'purchaseOrderNumber': 'PO-2026-001',
  'status': 'PartiallyReceived',
  'supplierName': 'Acme Supplies',
  'supplierReference': 'ACME-42',
  'lineCount': 3,
  'expectedQuantity': 12,
  'receivedQuantity': 7,
  'expectedTotal': 1240.5,
  'createdAt': '2026-07-01T10:00:00.000Z',
};
