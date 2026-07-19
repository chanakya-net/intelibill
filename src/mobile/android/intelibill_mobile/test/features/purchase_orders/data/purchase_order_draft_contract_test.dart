import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/create_purchase_order_draft_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  setUpAll(() {
    registerFallbackValue(const CreatePurchaseOrderDraftRequestDto());
  });

  test('serializes create draft request with date-only fields and nulls', () {
    const request = CreatePurchaseOrderDraftRequestDto(
      orderDate: '2026-07-19',
      notes: 'Restock',
    );

    expect(request.toJson(), {
      'supplierId': null,
      'orderDate': '2026-07-19',
      'expectedDeliveryDate': null,
      'supplierReferenceNumber': null,
      'notes': 'Restock',
      'supplierName': null,
      'supplierReference': null,
      'lines': <dynamic>[],
    });
  });

  test('repository trims optional blanks to null and formats dates', () async {
    final remote = _MockPurchaseOrderRemoteDataSource();
    final repository = PurchaseOrderRepositoryImpl(remote);
    when(() => remote.createDraft(any())).thenAnswer((_) async => _detailDto());

    await repository.createDraft(
      PurchaseOrderDraft(
        supplierId: '  supplier-1  ',
        orderDate: DateTime(2026, 7, 19),
        expectedDeliveryDate: DateTime(2026, 7, 21),
        supplierReferenceNumber: '   ',
        notes: '  Notes  ',
      ),
    );

    verify(
      () => remote.createDraft(
        const CreatePurchaseOrderDraftRequestDto(
          supplierId: 'supplier-1',
          orderDate: '2026-07-19',
          expectedDeliveryDate: '2026-07-21',
          notes: 'Notes',
        ),
      ),
    ).called(1);
  });

  test('remote data source posts the complete create draft JSON', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    const request = CreatePurchaseOrderDraftRequestDto(
      supplierId: 'supplier-1',
      orderDate: '2026-07-19',
      expectedDeliveryDate: '2026-07-21',
      supplierReferenceNumber: 'REF-1',
      notes: 'Notes',
    );
    when(
      () => apiClient.post<Map<String, dynamic>>(
        any<String>(),
        data: any<Map<String, dynamic>>(named: 'data'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: _detailJson(),
        statusCode: 201,
        requestOptions: RequestOptions(path: '/purchase-orders'),
      ),
    );

    await source.createDraft(request);

    verify(
      () => apiClient.post<Map<String, dynamic>>(
        '/purchase-orders',
        data: request.toJson(),
      ),
    ).called(1);
  });

  test('update uses the create draft body without a shop identifier', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    const request = CreatePurchaseOrderDraftRequestDto(
      supplierId: 'supplier-1',
      orderDate: '2026-07-19',
      expectedDeliveryDate: '2026-07-21',
      supplierReferenceNumber: 'REF-1',
      notes: 'Notes',
    );
    when(
      () => apiClient.put<Map<String, dynamic>>(
        any<String>(),
        data: any<Map<String, dynamic>>(named: 'data'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: _detailJson(),
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-1'),
      ),
    );

    await source.updateDraft('po-1', request);

    verify(
      () => apiClient.put<Map<String, dynamic>>(
        '/purchase-orders/po-1',
        data: request.toJson(),
      ),
    ).called(1);
    expect(request.toJson(), isNot(contains('shopId')));
  });

  test('repository updates with the same normalized body as create', () async {
    final remote = _MockPurchaseOrderRemoteDataSource();
    final repository = PurchaseOrderRepositoryImpl(remote);
    when(
      () => remote.updateDraft('po-1', any()),
    ).thenAnswer((_) async => _detailDto());

    await repository.updateDraft(
      'po-1',
      PurchaseOrderDraft(
        supplierId: '  supplier-1  ',
        orderDate: DateTime(2026, 7, 19),
        notes: '  Notes  ',
      ),
    );

    verify(
      () => remote.updateDraft(
        'po-1',
        const CreatePurchaseOrderDraftRequestDto(
          supplierId: 'supplier-1',
          orderDate: '2026-07-19',
          notes: 'Notes',
        ),
      ),
    ).called(1);
  });

  test('repository maps update serialization failures', () async {
    final remote = _MockPurchaseOrderRemoteDataSource();
    final repository = PurchaseOrderRepositoryImpl(remote);
    when(
      () => remote.updateDraft('po-1', any()),
    ).thenThrow(const FormatException('Malformed response'));

    expect(
      () => repository.updateDraft('po-1', const PurchaseOrderDraft()),
      throwsA(
        isA<AppException>().having(
          (error) => error.failure,
          'failure',
          const Failure.serialization(message: 'Malformed response'),
        ),
      ),
    );
  });
}

class _MockPurchaseOrderRemoteDataSource extends Mock
    implements PurchaseOrderRemoteDataSource {}

Map<String, dynamic> _detailJson() => {
  'purchaseOrderId': 'po-1',
  'purchaseOrderNumber': 'PO-2026-000001',
  'status': 'Draft',
  'supplierId': null,
  'orderDate': null,
  'expectedDeliveryDate': null,
  'supplierReferenceNumber': null,
  'notes': null,
  'lines': <dynamic>[],
  'expectedTotal': 0,
  'createdAt': '2026-07-19T10:00:00Z',
  'supplierName': null,
  'supplierReference': null,
  'receivedQuantity': 0,
  'cancellationReason': null,
  'closedAt': null,
  'closedBy': null,
  'closeReason': null,
  'receipts': <dynamic>[],
};

PurchaseOrderDetailDto _detailDto() =>
    PurchaseOrderDetailDto.fromJson(_detailJson());
