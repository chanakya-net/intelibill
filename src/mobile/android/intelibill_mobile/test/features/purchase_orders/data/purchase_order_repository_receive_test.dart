import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/receive_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRemoteDataSource extends Mock
    implements PurchaseOrderRemoteDataSource {}

void main() {
  setUpAll(() {
    registerFallbackValue(_receiveRequest());
  });

  late MockPurchaseOrderRemoteDataSource dataSource;
  late PurchaseOrderRepositoryImpl repository;

  setUp(() {
    dataSource = MockPurchaseOrderRemoteDataSource();
    repository = PurchaseOrderRepositoryImpl(dataSource);
  });

  test('maps input to request DTO and trims optional headers', () async {
    when(
      () => dataSource.receive(any(), any()),
    ).thenAnswer((_) async => _detailDto());

    await repository.receive(
      'po-1',
      ReceivePurchaseOrderInput(
        referenceNumber: '  PO-REF  ',
        notes: '  notes  ',
        receivedAt: DateTime.utc(2026, 7, 19, 8, 30),
        lines: const [
          ReceivePurchaseOrderLineInput(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR-1',
            batchNumber: 'BN-1',
            quantity: 3,
            totalPurchaseCost: 120,
            unitCost: 40,
            mrp: 150,
            salesPrice: 150,
            taxRatePercent: 5,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        ],
      ),
    );

    final captured =
        verify(
              () => dataSource.receive(
                'po-1',
                captureAny<ReceivePurchaseOrderRequestDto>(),
              ),
            ).captured.single
            as ReceivePurchaseOrderRequestDto;

    expect(captured.referenceNumber, 'PO-REF');
    expect(captured.notes, 'notes');
    expect(captured.lines, hasLength(1));
    expect(captured.lines[0].quantity, 3);
  });

  test('maps response DTO to domain purchase order', () async {
    when(
      () => dataSource.receive(any(), any()),
    ).thenAnswer((_) async => _detailDto());

    final result = await repository.receive(
      'po-1',
      ReceivePurchaseOrderInput(
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: const [
          ReceivePurchaseOrderLineInput(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR-1',
            batchNumber: 'BN-1',
            quantity: 3,
            totalPurchaseCost: 120,
            unitCost: 40,
            mrp: 150,
            salesPrice: 150,
            taxRatePercent: 5,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        ],
      ),
    );

    expect(result, isA<PurchaseOrder>());
    expect(result.purchaseOrderId, 'po-1');
    expect(result.status, PurchaseOrderStatus.received);
    expect(result.receivedQuantity, 5);
  });

  test('rejects empty line lists before hitting data source', () {
    expect(
      () => repository.receive(
        'po-1',
        ReceivePurchaseOrderInput(
          receivedAt: DateTime.utc(2026, 7, 19),
          lines: const [],
        ),
      ),
      throwsA(isA<AppException>()),
    );

    verifyNever(() => dataSource.receive(any(), any()));
  });

  test('rejects duplicate line IDs before hitting data source', () {
    expect(
      () => repository.receive(
        'po-1',
        ReceivePurchaseOrderInput(
          receivedAt: DateTime.utc(2026, 7, 19),
          lines: const [
            ReceivePurchaseOrderLineInput(
              purchaseOrderLineId: 'line-1',
              barcode: 'BAR-1',
              batchNumber: 'BN-1',
              quantity: 1,
              totalPurchaseCost: 1,
              unitCost: 1,
              mrp: 1,
              salesPrice: 1,
              taxRatePercent: 1,
              taxIncluded: false,
              purchaseTaxIncluded: false,
            ),
            ReceivePurchaseOrderLineInput(
              purchaseOrderLineId: 'line-1',
              barcode: 'BAR-1',
              batchNumber: 'BN-1',
              quantity: 2,
              totalPurchaseCost: 1,
              unitCost: 1,
              mrp: 1,
              salesPrice: 1,
              taxRatePercent: 1,
              taxIncluded: false,
              purchaseTaxIncluded: false,
            ),
          ],
        ),
      ),
      throwsA(isA<AppException>()),
    );

    verifyNever(() => dataSource.receive(any(), any()));
  });

  test(
    'throws unknown failure when datasource throws a non-app error',
    () async {
      var calls = 0;
      when(
        () => dataSource.receive(any(), any()),
      ).thenAnswer((_) {
        calls += 1;
        throw Exception('network');
      });

      final future = repository.receive(
        'po-1',
        ReceivePurchaseOrderInput(
          receivedAt: DateTime.utc(2026, 7, 19),
          lines: const [
            ReceivePurchaseOrderLineInput(
              purchaseOrderLineId: 'line-1',
              barcode: 'BAR-1',
              batchNumber: 'BN-1',
              quantity: 1,
              totalPurchaseCost: 1,
              unitCost: 1,
              mrp: 1,
              salesPrice: 1,
              taxRatePercent: 1,
              taxIncluded: false,
              purchaseTaxIncluded: false,
            ),
          ],
        ),
      );
      await expectLater(future, throwsA(isA<AppException>()));
      expect(calls, equals(1));
    },
  );

  test('wraps AppException from data source', () {
    when(
      () => dataSource.receive(any(), any()),
    ).thenThrow(AppException(failure: const Failure.unknown(message: 'fail')));

    expect(
      () => repository.receive(
        'po-1',
        ReceivePurchaseOrderInput(
          receivedAt: DateTime.utc(2026, 7, 19),
          lines: const [
            ReceivePurchaseOrderLineInput(
              purchaseOrderLineId: 'line-1',
              barcode: 'BAR-1',
              batchNumber: 'BN-1',
              quantity: 1,
              totalPurchaseCost: 1,
              unitCost: 1,
              mrp: 1,
              salesPrice: 1,
              taxRatePercent: 1,
              taxIncluded: false,
              purchaseTaxIncluded: false,
            ),
          ],
        ),
      ),
      throwsA(isA<AppException>()),
    );
  });
}

PurchaseOrderDetailDto _detailDto() => PurchaseOrderDetailDto(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-001',
  status: 'Received',
  lines: const [
    PurchaseOrderLineDto(
      lineId: 'line-1',
      itemId: 'item-1',
      description: 'Widget',
      expectedQuantity: 10,
      receivedQuantity: 5,
      remainingQuantity: 5,
      unitCost: 20,
      lineTotal: 100,
    ),
  ],
  expectedTotal: 100,
  createdAt: DateTime.utc(2026, 7),
  receivedQuantity: 5,
);

ReceivePurchaseOrderRequestDto _receiveRequest() =>
    ReceivePurchaseOrderRequestDto(
      receivedAt: DateTime.utc(2026, 7, 19),
      lines: [
        const ReceivePurchaseOrderLineRequestDto(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR-1',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 1,
          unitCost: 1,
          mrp: 1,
          salesPrice: 1,
          taxRatePercent: 1,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        ),
      ],
    );
