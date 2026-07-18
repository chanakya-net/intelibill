import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/mappers/purchase_order_mapper.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
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

  test(
    'GET /purchase-orders/{id} requests purchase-order detail endpoint',
    () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
      when(
        () => apiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>?>(named: 'queryParameters'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: _detailJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
        ),
      );

      await source.getPurchaseOrder('po-77');

      verify(
        () => apiClient.get<Map<String, dynamic>>('/purchase-orders/po-77'),
      ).called(1);
    },
  );

  test('maps purchase-order detail fields to domain values and aggregates', () {
    final domain = PurchaseOrderMapper.detailToDomain(
      PurchaseOrderDetailDto.fromJson(_detailJson()),
    );

    expect(domain.purchaseOrderId, 'po-77');
    expect(domain.purchaseOrderNumber, 'PO-2026-077');
    expect(domain.status, PurchaseOrderStatus.placed);
    expect(domain.supplierId, 'supplier-99');
    expect(domain.supplierName, 'Fresh Grocers');
    expect(domain.supplierReference, 'RG-77');
    expect(domain.supplierReferenceNumber, 'SRN-77');
    expect(domain.notes, 'Urgent restock');
    expect(domain.orderDate, DateTime(2026, 7, 11));
    expect(domain.expectedDeliveryDate, DateTime(2026, 7, 14));
    expect(domain.expectedTotal, 1450.5);
    expect(
      domain.createdAt,
      DateTime.parse('2026-07-01T03:00:00.000-05:00').toLocal(),
    );
    expect(domain.lines, hasLength(2));
    expect(domain.expectedQuantity, 18);
    expect(domain.receivedQuantity, 7);
    expect(domain.remainingQuantity, 11);
  });

  test('maps receipt history and lifecycle metadata', () {
    final domain = PurchaseOrderMapper.detailToDomain(
      PurchaseOrderDetailDto.fromJson(_detailWithReceiptJson()),
    );

    expect(domain.cancellationReason, 'Supplier unavailable');
    expect(domain.closedAt, DateTime.parse('2026-07-15T10:30:00Z').toLocal());
    expect(domain.closedBy, 'user-closed');
    expect(domain.closeReason, 'Remaining stock discontinued');
    final receipt = hasLength(1);
    expect(domain.receipts, receipt);
    final mappedReceipt = domain.receipts.single;
    expect(mappedReceipt.receiptId, 'receipt-1');
    expect(mappedReceipt.receiptNumber, 'GRN-001');
    expect(
      mappedReceipt.receivedAt,
      DateTime.parse('2026-07-14T06:00:00Z').toLocal(),
    );
    expect(mappedReceipt.receivedByUserId, 'user-receiver');
    expect(mappedReceipt.receivedByDisplayName, 'Riya Receiver');
    expect(mappedReceipt.referenceNumber, 'REF-001');
    expect(mappedReceipt.notes, 'Counted at dock');

    final line = mappedReceipt.lines.single;
    expect(line.receiptLineId, 'receipt-line-1');
    expect(line.purchaseOrderLineId, 'line-1');
    expect(line.itemId, 'item-1');
    expect(line.inventoryBatchId, 'batch-1');
    expect(line.batchNumber, 'BATCH-001');
    expect(line.batchVoided, isTrue);
    expect(line.stockTransactionId, 'transaction-1');
    expect(line.quantity, 2.5);
    expect(line.totalPurchaseCost, 250);
    expect(line.unitCost, 100);
    expect(line.mrp, 150);
    expect(line.salesPrice, 125);
    expect(line.taxRatePercent, 5);
    expect(line.taxIncluded, isFalse);
    expect(line.purchaseTaxIncluded, isTrue);
  });

  test('maps null and empty receipt history to an empty list', () {
    final nullReceipts = PurchaseOrderMapper.detailToDomain(
      PurchaseOrderDetailDto.fromJson(_detailJsonNullables()),
    );
    final emptyReceipts = PurchaseOrderMapper.detailToDomain(
      PurchaseOrderDetailDto.fromJson(<String, dynamic>{
        ..._detailJson(),
        'receipts': <dynamic>[],
      }),
    );

    expect(nullReceipts.receipts, isEmpty);
    expect(emptyReceipts.receipts, isEmpty);
  });

  test('accepts null optional date and text fields', () {
    final domain = PurchaseOrderMapper.detailToDomain(
      PurchaseOrderDetailDto.fromJson(_detailJsonNullables()),
    );

    expect(domain.orderDate, isNull);
    expect(domain.expectedDeliveryDate, isNull);
    expect(domain.supplierReferenceNumber, isNull);
    expect(domain.notes, isNull);
  });

  test('maps an invalid detail status into serialization failure', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRepositoryImpl(
      PurchaseOrderRemoteDataSourceImpl(apiClient),
    );

    when(
      () => apiClient.get<Map<String, dynamic>>(any<String>()),
    ).thenAnswer(
      (_) async => Response(
        data: _detailInvalidStatusJson(),
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
      ),
    );

    await expectLater(
      source.getPurchaseOrder('po-77'),
      throwsA(
        isA<AppException>().having(
          (e) => e.failure,
          'failure',
          isA<SerializationFailure>(),
        ),
      ),
    );
  });

  test('mapper rejects an overflow orderDate that parse would normalize', () {
    final dto = PurchaseOrderDetailDto.fromJson(_detailOverflowOrderDateJson());

    expect(
      () => PurchaseOrderMapper.detailToDomain(dto),
      throwsFormatException,
    );
  });

  test('maps an overflow orderDate into serialization failure', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRepositoryImpl(
      PurchaseOrderRemoteDataSourceImpl(apiClient),
    );

    when(
      () => apiClient.get<Map<String, dynamic>>(any<String>()),
    ).thenAnswer(
      (_) async => Response(
        data: _detailOverflowOrderDateJson(),
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
      ),
    );

    await expectLater(
      source.getPurchaseOrder('po-77'),
      throwsA(
        isA<AppException>().having(
          (e) => e.failure,
          'failure',
          isA<SerializationFailure>(),
        ),
      ),
    );
  });

  test('maps a year-zero orderDate into serialization failure', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRepositoryImpl(
      PurchaseOrderRemoteDataSourceImpl(apiClient),
    );

    when(
      () => apiClient.get<Map<String, dynamic>>(any<String>()),
    ).thenAnswer(
      (_) async => Response(
        data: {..._detailJson(), 'orderDate': '0000-01-01'},
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
      ),
    );

    await expectLater(
      source.getPurchaseOrder('po-77'),
      throwsA(
        isA<AppException>().having(
          (e) => e.failure,
          'failure',
          isA<SerializationFailure>(),
        ),
      ),
    );
  });

  test(
    'maps an overflow expectedDeliveryDate into serialization failure',
    () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRepositoryImpl(
        PurchaseOrderRemoteDataSourceImpl(apiClient),
      );

      when(
        () => apiClient.get<Map<String, dynamic>>(any<String>()),
      ).thenAnswer(
        (_) async => Response(
          data: _detailOverflowDeliveryDateJson(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
        ),
      );

      await expectLater(
        source.getPurchaseOrder('po-77'),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<SerializationFailure>(),
          ),
        ),
      );
    },
  );

  test(
    'maps missing received-quantity detail to serialization failure',
    () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRepositoryImpl(
        PurchaseOrderRemoteDataSourceImpl(apiClient),
      );

      when(
        () => apiClient.get<Map<String, dynamic>>(any<String>()),
      ).thenAnswer(
        (_) async => Response(
          data: _detailWithoutReceivedQuantity(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
        ),
      );

      await expectLater(
        source.getPurchaseOrder('po-77'),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<SerializationFailure>(),
          ),
        ),
      );
    },
  );

  test(
    'maps malformed detail list payload into serialization failure',
    () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRepositoryImpl(
        PurchaseOrderRemoteDataSourceImpl(apiClient),
      );

      when(
        () => apiClient.get<Map<String, dynamic>>(any<String>()),
      ).thenAnswer(
        (_) async => Response(
          data: _detailMalformedLines(),
          statusCode: 200,
          requestOptions: RequestOptions(path: '/purchase-orders/po-77'),
        ),
      );

      await expectLater(
        source.getPurchaseOrder('po-77'),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<SerializationFailure>(),
          ),
        ),
      );
    },
  );

  test(
    'maps 404 as not-found for missing and foreign purchase-order fetch',
    () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
      when(() => apiClient.get<Map<String, dynamic>>(any<String>())).thenThrow(
        AppException(
          failure: const Failure.notFound(message: 'Purchase order not found'),
        ),
      );

      await expectLater(
        source.getPurchaseOrder('po-77'),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<NotFoundFailure>(),
          ),
        ),
      );

      when(() => apiClient.get<Map<String, dynamic>>(any<String>())).thenThrow(
        AppException(
          failure: const Failure.notFound(message: 'Purchase order not found'),
        ),
      );

      await expectLater(
        source.getPurchaseOrder('po-cross-shop'),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<NotFoundFailure>(),
          ),
        ),
      );
    },
  );

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

  test('sends status under exact `status` key', () async {
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
      PurchaseOrderFilters(status: PurchaseOrderStatus.placed),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {
          'page': 1,
          'page_size': 20,
          'status': 'Placed',
        },
      ),
    ).called(1);
  });

  test('omits null status from query params', () async {
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
      const PurchaseOrderFilters(status: null),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {
          'page': 1,
          'page_size': 20,
        },
      ),
    ).called(1);
  });

  test(
    'sends orderDateFrom in yyyy-MM-dd format under `order_date_from` key',
    () async {
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
        PurchaseOrderFilters(orderDateFrom: DateTime(2026, 3, 15)),
      );

      verify(
        () => apiClient.get<Map<String, dynamic>>(
          '/purchase-orders',
          queryParameters: {
            'page': 1,
            'page_size': 20,
            'order_date_from': '2026-03-15',
          },
        ),
      ).called(1);
    },
  );

  test(
    'sends orderDateTo in yyyy-MM-dd format under `order_date_to` key',
    () async {
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
        PurchaseOrderFilters(orderDateTo: DateTime(2026, 7, 20)),
      );

      verify(
        () => apiClient.get<Map<String, dynamic>>(
          '/purchase-orders',
          queryParameters: {
            'page': 1,
            'page_size': 20,
            'order_date_to': '2026-07-20',
          },
        ),
      ).called(1);
    },
  );

  test('sends both date filters together with status and search', () async {
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
      PurchaseOrderFilters(
        search: 'widget',
        status: PurchaseOrderStatus.received,
        orderDateFrom: DateTime(2026, 1, 1),
        orderDateTo: DateTime(2026, 12, 31),
      ),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {
          'page': 1,
          'page_size': 20,
          'search': 'widget',
          'status': 'Received',
          'order_date_from': '2026-01-01',
          'order_date_to': '2026-12-31',
        },
      ),
    ).called(1);
  });

  test('omits null dates from query params', () async {
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
      const PurchaseOrderFilters(
        orderDateFrom: null,
        orderDateTo: null,
      ),
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/purchase-orders',
        queryParameters: {
          'page': 1,
          'page_size': 20,
        },
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

Map<String, dynamic> _detailJson() => {
  'purchaseOrderId': 'po-77',
  'purchaseOrderNumber': 'PO-2026-077',
  'status': 'Placed',
  'supplierId': 'supplier-99',
  'orderDate': '2026-07-11',
  'expectedDeliveryDate': '2026-07-14',
  'supplierReferenceNumber': 'SRN-77',
  'notes': 'Urgent restock',
  'lines': [
    {
      'lineId': 'line-1',
      'itemId': 'item-1',
      'description': 'Widget A',
      'expectedQuantity': 10,
      'receivedQuantity': 3,
      'remainingQuantity': 7,
      'unitCost': 75,
      'lineTotal': 750,
    },
    {
      'lineId': 'line-2',
      'itemId': 'item-2',
      'description': 'Widget B',
      'expectedQuantity': 8,
      'receivedQuantity': 4,
      'remainingQuantity': 4,
      'unitCost': 87.5,
      'lineTotal': 700.5,
    },
  ],
  'expectedTotal': 1450.5,
  'createdAt': '2026-07-01T03:00:00.000-05:00',
  'supplierName': 'Fresh Grocers',
  'supplierReference': 'RG-77',
  'receivedQuantity': 7,
};

Map<String, dynamic> _detailJsonNullables() => {
  ..._detailJson(),
  'orderDate': null,
  'expectedDeliveryDate': null,
  'supplierReferenceNumber': null,
  'notes': null,
};

Map<String, dynamic> _detailWithReceiptJson() => {
  ..._detailJson(),
  'status': 'Closed',
  'cancellationReason': 'Supplier unavailable',
  'closedAt': '2026-07-15T10:30:00Z',
  'closedBy': 'user-closed',
  'closeReason': 'Remaining stock discontinued',
  'receipts': [
    {
      'receiptId': 'receipt-1',
      'receiptNumber': 'GRN-001',
      'receivedAt': '2026-07-14T06:00:00Z',
      'referenceNumber': 'REF-001',
      'notes': 'Counted at dock',
      'receivedByUserId': 'user-receiver',
      'receivedByDisplayName': 'Riya Receiver',
      'lines': [
        {
          'receiptLineId': 'receipt-line-1',
          'purchaseOrderLineId': 'line-1',
          'itemId': 'item-1',
          'inventoryBatchId': 'batch-1',
          'batchNumber': 'BATCH-001',
          'batchVoided': true,
          'stockTransactionId': 'transaction-1',
          'quantity': 2.5,
          'totalPurchaseCost': 250,
          'unitCost': 100,
          'mrp': 150,
          'salesPrice': 125,
          'taxRatePercent': 5,
          'taxIncluded': false,
          'purchaseTaxIncluded': true,
        },
      ],
    },
  ],
};

Map<String, dynamic> _detailInvalidStatusJson() => {
  ..._detailJson(),
  'status': 'DoesNotExist',
};

Map<String, dynamic> _detailOverflowOrderDateJson() => {
  ..._detailJson(),
  'orderDate': '2026-14-99',
};

Map<String, dynamic> _detailOverflowDeliveryDateJson() => {
  ..._detailJson(),
  'expectedDeliveryDate': '2026-02-30',
};

Map<String, dynamic> _detailWithoutReceivedQuantity() {
  final json = _detailJson();
  json.remove('receivedQuantity');
  return json;
}

Map<String, dynamic> _detailMalformedLines() => {
  ..._detailJson(),
  'lines': 'not-a-list',
};
