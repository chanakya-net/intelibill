import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/data_sources/inventory_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_row_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/adjust_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/create_item_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/generate_item_barcode_response_dto.dart';
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
      test('calls /items endpoint and returns parsed catalog items', () async {
        final responseData = {
          'items': [
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
          ],
          'totalCount': 2,
          'pageNumber': 1,
          'pageSize': 100,
        };

        when(
          () => mockApiClient.get<Map<String, dynamic>>(
            any<String>(),
            queryParameters: any<Map<String, dynamic>>(
              named: 'queryParameters',
            ),
          ),
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

        verify(
          () => mockApiClient.get<Map<String, dynamic>>(
            '/items',
            queryParameters: {'pageNumber': 1, 'pageSize': 100},
          ),
        ).called(1);
      });

      test('fetches additional pages when catalog is paginated', () async {
        when(
          () => mockApiClient.get<Map<String, dynamic>>(
            any<String>(),
            queryParameters: any<Map<String, dynamic>>(
              named: 'queryParameters',
            ),
          ),
        ).thenAnswer((invocation) async {
          final query =
              invocation.namedArguments[#queryParameters]
                  as Map<String, dynamic>;
          final pageNumber = query['pageNumber'] as int;

          if (pageNumber == 1) {
            return Response(
              data: {
                'items': [
                  {
                    'id': 'item-1',
                    'name': 'Rice Basmati',
                    'barcode': '8901234567890',
                    'uom': 'KG',
                    'isActive': true,
                    'currentStock': 100.0,
                  },
                ],
                'totalCount': 2,
                'pageNumber': 1,
                'pageSize': 1,
              },
              statusCode: 200,
              requestOptions: RequestOptions(path: '/items'),
            );
          }

          return Response(
            data: {
              'items': [
                {
                  'id': 'item-2',
                  'name': 'Sugar',
                  'barcode': '8901234567891',
                  'uom': 'KG',
                  'isActive': false,
                  'currentStock': 50.0,
                },
              ],
              'totalCount': 2,
              'pageNumber': 2,
              'pageSize': 1,
            },
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items'),
          );
        });

        final dtos = await remoteDataSource.getItems();

        expect(dtos.length, 2);
        expect(dtos.map((dto) => dto.id), ['item-1', 'item-2']);
        verify(
          () => mockApiClient.get<Map<String, dynamic>>(
            '/items',
            queryParameters: any(named: 'queryParameters'),
          ),
        ).called(2);
      });

      test('returns empty list when API returns empty catalog', () async {
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
              'pageNumber': 1,
              'pageSize': 100,
            },
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

    group('getProductDetails', () {
      test('calls /items/details with name and barcode query', () async {
        final responseData = {
          'name': 'Rice Basmati',
          'description': 'Premium rice',
          'uom': 'KG',
          'costPrice': 42.0,
          'mrp': 50.0,
          'salesPrice': 48.0,
          'supplierId': 'supplier-1',
          'supplierName': 'Acme Foods',
          'taxIncluded': true,
          'taxRatePercent': 18.0,
        };

        when(
          () => mockApiClient.get<Map<String, dynamic>>(
            any<String>(),
            queryParameters: any<Map<String, dynamic>>(
              named: 'queryParameters',
            ),
          ),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items/details'),
          ),
        );

        final dto = await remoteDataSource.getProductDetails(
          name: ' Rice Basmati ',
          barcode: ' 8901234567890 ',
        );

        expect(dto.name, 'Rice Basmati');
        expect(dto.costPrice, 42.0);
        expect(dto.taxIncluded, isTrue);

        verify(
          () => mockApiClient.get<Map<String, dynamic>>(
            '/items/details',
            queryParameters: {
              'name': 'Rice Basmati',
              'barcode': '8901234567890',
            },
          ),
        ).called(1);
      });
    });

    group('updateItem', () {
      test('patches /items/:id with 204 and returns void', () async {
        const request = UpdateItemRequestDto(
          name: 'Rice Basmati Premium',
          barcode: '8901234567890',
          uom: 'KG',
          isActive: false,
        );

        when(
          () => mockApiClient.patch<void>(
            any<String>(),
            data: any<Map<String, dynamic>>(named: 'data'),
          ),
        ).thenAnswer(
          (_) async => Response(
            statusCode: 204,
            requestOptions: RequestOptions(path: '/items/item-1'),
          ),
        );

        await remoteDataSource.updateItem('item-1', request);

        verify(
          () => mockApiClient.patch<void>(
            '/items/item-1',
            data: request.toJson(),
          ),
        ).called(1);
      });
    });

    group('getInventoryBatches', () {
      test(
        'calls /inventory/batches endpoint and returns parsed DTOs',
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
        },
      );

      test('returns empty list when API returns empty array', () async {
        when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
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
            queryParameters: any<Map<String, dynamic>>(
              named: 'queryParameters',
            ),
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
      test(
        'posts to /inventory/inbound/batch and returns response DTO',
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
              requestOptions: RequestOptions(path: '/inventory/inbound/batch'),
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
        },
      );
    });

    group('generateItemBarcode', () {
      test('calls endpoint with no body and maps barcode response', () async {
        when(
          () => mockApiClient.post<Map<String, dynamic>>(any<String>()),
        ).thenAnswer(
          (_) async => Response(
            data: {'barcode': 'IB-000001'},
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items/barcodes/generate'),
          ),
        );

        final dto = await remoteDataSource.generateItemBarcode();

        expect(dto, isA<GenerateItemBarcodeResponseDto>());
        expect(dto.barcode, 'IB-000001');
        verify(
          () => mockApiClient.post<Map<String, dynamic>>(
            '/items/barcodes/generate',
          ),
        ).called(1);
      });

      test('throws malformed payload as FormatException from Dio contract', () {
        when(
          () => mockApiClient.post<Map<String, dynamic>>(any<String>()),
        ).thenAnswer(
          (_) async => Response(
            data: {'code': 'BAD'},
            statusCode: 200,
            requestOptions: RequestOptions(path: '/items/barcodes/generate'),
          ),
        );

        expect(
          remoteDataSource.generateItemBarcode(),
          throwsA(isA<TypeError>()),
        );
      });
    });
  });
}
