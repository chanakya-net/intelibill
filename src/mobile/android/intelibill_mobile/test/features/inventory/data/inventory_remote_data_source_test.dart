import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/data_sources/inventory_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_row_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/adjust_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/create_item_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/update_item_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late InventoryRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = InventoryRemoteDataSourceImpl(mockApiClient);
  });

  group('InventoryRemoteDataSourceImpl', () {
    group('getItems', () {
      test('calls /items endpoint and returns parsed DTOs', () async {
        final responseData = [
          {
            'id': 'item-1',
            'name': 'Rice Basmati',
            'barcode': '8901234567890',
            'description': 'Premium rice',
            'uom': 'KG',
            'isActive': true,
            'currentStock': 100.0,
          },
          {
            'id': 'item-2',
            'name': 'Sugar',
            'barcode': '8901234567891',
            'uom': 'KG',
            'isActive': false,
            'currentStock': 50.0,
          },
        ];

        when(
          () => mockApiClient.get<List<dynamic>>(any<String>()),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items'),
          ),
        );

        final dtos = await remoteDataSource.getItems();

        expect(dtos.length, 2);
        expect(dtos[0].id, 'item-1');
        expect(dtos[0].name, 'Rice Basmati');
        expect(dtos[1].id, 'item-2');
        expect(dtos[1].description, isNull);

        verify(() => mockApiClient.get<List<dynamic>>('/items')).called(1);
      });

      test('returns empty list when API returns empty array', () async {
        when(
          () => mockApiClient.get<List<dynamic>>(any<String>()),
        ).thenAnswer(
          (_) async => Response(
            data: <dynamic>[],
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items'),
          ),
        );

        final dtos = await remoteDataSource.getItems();

        expect(dtos, isEmpty);
      });
    });

    group('createItem', () {
      test('posts to /items and returns created item DTO', () async {
        const request = CreateItemRequestDto(
          name: 'Rice Basmati',
          barcode: '8901234567890',
          uom: 'KG',
          isActive: true,
        );
        final responseData = {
          'id': 'item-new',
          'name': 'Rice Basmati',
          'barcode': '8901234567890',
          'uom': 'KG',
          'isActive': true,
          'currentStock': 0.0,
        };

        when(
          () => mockApiClient.post<Map<String, dynamic>>(
            any<String>(),
            data: any<Map<String, dynamic>>(named: 'data'),
          ),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items'),
          ),
        );

        final dto = await remoteDataSource.createItem(request);

        expect(dto.id, 'item-new');
        expect(dto.name, 'Rice Basmati');

        verify(
          () => mockApiClient.post<Map<String, dynamic>>(
            '/items',
            data: request.toJson(),
          ),
        ).called(1);
      });
    });

    group('updateItem', () {
      test('patches /items/:id and returns updated item DTO', () async {
        const request = UpdateItemRequestDto(
          name: 'Rice Basmati Premium',
          barcode: '8901234567890',
          uom: 'KG',
          isActive: false,
        );
        final responseData = {
          'id': 'item-1',
          'name': 'Rice Basmati Premium',
          'barcode': '8901234567890',
          'uom': 'KG',
          'isActive': false,
          'currentStock': 100.0,
        };

        when(
          () => mockApiClient.patch<Map<String, dynamic>>(
            any<String>(),
            data: any<Map<String, dynamic>>(named: 'data'),
          ),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items/item-1'),
          ),
        );

        final dto = await remoteDataSource.updateItem('item-1', request);

        expect(dto.id, 'item-1');
        expect(dto.name, 'Rice Basmati Premium');
        expect(dto.isActive, false);

        verify(
          () => mockApiClient.patch<Map<String, dynamic>>(
            '/items/item-1',
            data: request.toJson(),
          ),
        ).called(1);
      });
    });

    group('getInventoryBatches', () {
      test('calls /inventory/batches endpoint and returns parsed DTOs',
          () async {
        final responseData = [
          {
            'id': 'batch-1',
            'itemId': 'item-1',
            'itemName': 'Rice Basmati',
            'barcode': '8901234567890',
            'batchNumber': 'BN-001',
            'quantity': 100.0,
            'costPrice': 45.0,
            'mrp': 60.0,
            'salesPrice': 55.0,
            'isVoided': false,
            'createdAt': '2024-01-15T10:30:00.000Z',
          },
        ];

        when(
          () => mockApiClient.get<List<dynamic>>(any<String>()),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/inventory/batches'),
          ),
        );

        final dtos = await remoteDataSource.getInventoryBatches();

        expect(dtos.length, 1);
        expect(dtos[0].id, 'batch-1');
        expect(dtos[0].batchNumber, 'BN-001');

        verify(
          () => mockApiClient.get<List<dynamic>>('/inventory/batches'),
        ).called(1);
      });

      test('returns empty list when API returns empty array', () async {
        when(
          () => mockApiClient.get<List<dynamic>>(any<String>()),
        ).thenAnswer(
          (_) async => Response(
            data: <dynamic>[],
            statusCode: 200,
            requestOptions: RequestOptions(path: '/inventory/batches'),
          ),
        );

        final dtos = await remoteDataSource.getInventoryBatches();

        expect(dtos, isEmpty);
      });
    });

    group('adjustInventoryBatch', () {
      test('posts to /inventory/batches/:id/adjust', () async {
        const request = AdjustInventoryBatchRequestDto(
          direction: 'Add',
          reason: 'StockCount',
          quantity: 10,
        );

        when(
          () => mockApiClient.post<void>(
            any<String>(),
            data: any<Map<String, dynamic>>(named: 'data'),
          ),
        ).thenAnswer(
          (_) async => Response(
            statusCode: 200,
            requestOptions: RequestOptions(
              path: '/inventory/batches/batch-1/adjust',
            ),
          ),
        );

        await remoteDataSource.adjustInventoryBatch('batch-1', request);

        verify(
          () => mockApiClient.post<void>(
            '/inventory/batches/batch-1/adjust',
            data: request.toJson(),
          ),
        ).called(1);
      });
    });

    group('getAdjustmentHistory', () {
      test('calls /inventory/adjustments with pagination params', () async {
        final responseData = {
          'items': [
            {
              'adjustmentId': 'adj-1',
              'batchId': 'batch-1',
              'itemId': 'item-1',
              'itemName': 'Rice',
              'batchNumber': 'BN-001',
              'direction': 'Add',
              'reason': 'StockCount',
              'quantity': 10.0,
              'costImpact': 450.0,
              'performedAt': '2024-06-15T09:00:00.000Z',
              'performedByDisplayName': 'John',
            },
          ],
          'totalCount': 1,
          'pageNumber': 1,
          'pageSize': 25,
        };

        when(
          () => mockApiClient.get<Map<String, dynamic>>(
            any<String>(),
            queryParameters:
                any<Map<String, dynamic>>(named: 'queryParameters'),
          ),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/inventory/adjustments'),
          ),
        );

        final response = await remoteDataSource.getAdjustmentHistory(
          pageNumber: 1,
          pageSize: 25,
        );

        expect(response.items.length, 1);
        expect(response.totalCount, 1);
        expect(response.pageNumber, 1);
        expect(response.pageSize, 25);
        expect(response.items[0].adjustmentId, 'adj-1');

        verify(
          () => mockApiClient.get<Map<String, dynamic>>(
            '/inventory/adjustments',
            queryParameters: {'pageNumber': 1, 'pageSize': 25},
          ),
        ).called(1);
      });
    });

    group('addInventoryInbound', () {
      test('posts to /inventory/inbound/batch and returns response DTO',
          () async {
        const row = AddInventoryBatchRowDto(
          clientRowId: '1',
          itemName: 'Rice',
          barcode: '123',
          uom: 'KG',
          batchNumber: 'BN-001',
          quantity: 50,
          costPrice: 45,
          mrp: 60,
          salesPrice: 55,
          taxRatePercent: 0,
          taxIncluded: false,
        );
        const request = AddInventoryBatchRequestDto(items: [row]);
        final responseData = {
          'requestedCount': 1,
          'successCount': 1,
          'failedCount': 0,
          'succeeded': <Map<String, dynamic>>[],
          'failed': <Map<String, dynamic>>[],
        };

        when(
          () => mockApiClient.post<Map<String, dynamic>>(
            any<String>(),
            data: any<Map<String, dynamic>>(named: 'data'),
          ),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(
              path: '/inventory/inbound/batch',
            ),
          ),
        );

        final response = await remoteDataSource.addInventoryInbound(request);

        expect(response.requestedCount, 1);
        expect(response.successCount, 1);
        expect(response.failedCount, 0);

        verify(
          () => mockApiClient.post<Map<String, dynamic>>(
            '/inventory/inbound/batch',
            data: request.toJson(),
          ),
        ).called(1);
      });
    });
  });
}
