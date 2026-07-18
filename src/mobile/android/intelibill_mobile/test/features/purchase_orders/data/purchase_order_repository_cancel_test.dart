import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/cancel_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRemoteDataSource extends Mock
    implements PurchaseOrderRemoteDataSource {}

void main() {
  late MockPurchaseOrderRemoteDataSource mockDataSource;
  late PurchaseOrderRepositoryImpl repository;

  setUpAll(() {
    registerFallbackValue(CancelPurchaseOrderRequestDto(reason: ''));
  });

  setUp(() {
    mockDataSource = MockPurchaseOrderRemoteDataSource();
    repository = PurchaseOrderRepositoryImpl(mockDataSource);
  });

  group('PurchaseOrderRepositoryImpl.cancel', () {
    test('trims reason before sending to data source', () async {
      final dto = _cancelledDto();
      when(() => mockDataSource.cancel(any(), any()))
          .thenAnswer((_) async => dto);

      await repository.cancel('po-1', '  No longer needed  ');

      final captured = verify(
        () => mockDataSource.cancel('po-1', captureAny<CancelPurchaseOrderRequestDto>()),
      ).captured;
      expect(
        (captured[0] as CancelPurchaseOrderRequestDto).reason,
        'No longer needed',
      );
    });

    test('maps cancelled DTO to domain PurchaseOrder', () async {
      final dto = _cancelledDto();
      when(() => mockDataSource.cancel(any(), any())).thenAnswer((_) async => dto);

      final result = await repository.cancel('po-1', 'No longer needed');

      expect(result.purchaseOrderId, 'po-1');
      expect(result.status, PurchaseOrderStatus.cancelled);
      expect(result.cancellationReason, 'No longer needed');
    });

    test('wraps AppException from data source', () async {
      when(() => mockDataSource.cancel(any(), any()))
          .thenThrow(AppException(failure: const Failure.notFound()));

      expect(
        () => repository.cancel('po-1', 'No longer needed'),
        throwsA(isA<AppException>()),
      );
    });

    test('wraps unknown error', () async {
      when(() => mockDataSource.cancel(any(), any()))
          .thenThrow(Exception('network error'));

      expect(
        () => repository.cancel('po-1', 'No longer needed'),
        throwsA(isA<AppException>()),
      );
    });

    test('rejects blank reason and does not call remote data source', () async {
      expect(
        () => repository.cancel('po-1', '   '),
        throwsA(isA<AppException>()),
      );
      verifyNever(() => mockDataSource.cancel(any(), any()));
    });

    test('rejects 501-character reason and does not call remote data source', () async {
      final overlengthReason = 'a' * 501;
      expect(
        () => repository.cancel('po-1', overlengthReason),
        throwsA(isA<AppException>()),
      );
      verifyNever(() => mockDataSource.cancel(any(), any()));
    });
  });
}

PurchaseOrderDetailDto _cancelledDto() {
  return PurchaseOrderDetailDto(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: 'Cancelled',
    lines: const [],
    expectedTotal: 0.0,
    createdAt: DateTime.utc(2026, 7, 1),
    receivedQuantity: 0,
    cancellationReason: 'No longer needed',
  );
}
