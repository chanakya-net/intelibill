import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/receive_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  test('POSTs exactly one atomic request to the receipts path', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.post<Map<String, dynamic>>(
        any<String>(),
        data: any<Map<String, dynamic>>(named: 'data'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: _detailJson(),
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-1/receipts'),
      ),
    );

    await source.receive(
      'po-1',
      ReceivePurchaseOrderRequestDto(
        referenceNumber: 'PO-REF',
        notes: 'Manual receipt',
        receivedAt: DateTime.utc(2026, 7, 19, 8, 30),
        lines: [
          const ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR-001',
            batchNumber: 'BN-001',
            quantity: 2.5,
            totalPurchaseCost: 100,
            unitCost: 40,
            mrp: 120,
            salesPrice: 100,
            taxRatePercent: 5,
            taxIncluded: false,
            purchaseTaxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
          ),
        ],
      ),
    );

    verify(
      () => apiClient.post<Map<String, dynamic>>(
        '/purchase-orders/po-1/receipts',
        data: any<Map<String, dynamic>>(named: 'data'),
      ),
    ).called(1);
  });

  test('maps response to PurchaseOrderDetailDto', () async {
    final apiClient = MockApiClient();
    final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
    when(
      () => apiClient.post<Map<String, dynamic>>(
        any<String>(),
        data: any<Map<String, dynamic>>(named: 'data'),
      ),
    ).thenAnswer(
      (_) async => Response(
        data: _detailJson(),
        statusCode: 200,
        requestOptions: RequestOptions(path: '/purchase-orders/po-1/receipts'),
      ),
    );

    final result = await source.receive(
      'po-1',
      ReceivePurchaseOrderRequestDto(
        referenceNumber: null,
        notes: null,
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: const [
          ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR-001',
            batchNumber: 'BN-001',
            quantity: 1,
            totalPurchaseCost: 0,
            unitCost: 0,
            mrp: 0,
            salesPrice: 0,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        ],
      ),
    );

    expect(result, isA<PurchaseOrderDetailDto>());
    expect(result.purchaseOrderId, 'po-1');
    expect(result.status, 'Received');
  });
}

Map<String, dynamic> _detailJson() {
  return {
    'purchaseOrderId': 'po-1',
    'purchaseOrderNumber': 'PO-2026-001',
    'status': 'Received',
    'lines': [
      {
        'lineId': 'line-1',
        'itemId': 'item-1',
        'description': 'Widget',
        'expectedQuantity': 10,
        'receivedQuantity': 5,
        'remainingQuantity': 5,
        'unitCost': 20,
        'lineTotal': 100,
      },
    ],
    'expectedTotal': 100,
    'createdAt': '2026-07-01T10:00:00Z',
    'receivedQuantity': 5,
  };
}
